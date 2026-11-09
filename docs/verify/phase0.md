# Phase 0 验收记录：桌面层探针

日期：2026-11-09　机器：Windows 11 26200（单显示器 2560×1600，150% 缩放，dpi=144）
探针源码：`spike/DesktopLayerProbe/`

## 0. 环境准备（一次性）

开发机上原本没有 .NET SDK（只有 `C:\Program Files (x86)\dotnet` 下的 .NET 6 运行时），
按 AGENTS.md 装到用户目录，非管理员：

```powershell
# 已执行（dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"）
# 结果：
#   10.0.403 [C:\Users\ty\AppData\Local\Microsoft\dotnet\sdk]
#   Microsoft.WindowsDesktop.App 10.0.14 [C:\Users\ty\AppData\Local\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App]
```

**踩到的坑（后续所有命令都要注意）**：SDK 装在用户目录时，`bin\Debug\net10.0-windows\DesktopLayerProbe.exe`
直接双击会弹出 `DesktopLayerProbe.exe` 标题的错误框（apphost 找不到运行时），因为 apphost 只查
`Program Files\dotnet` 和注册表。必须先设：

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
```

## 1. 构建

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_NOLOGO='1'
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build spike\DesktopLayerProbe\DesktopLayerProbe.csproj -warnaserror -v m
```

```
DesktopLayerProbe -> E:\Dev\Project\wantodo\spike\DesktopLayerProbe\bin\Debug\net10.0-windows\DesktopLayerProbe.dll
已成功生成。 0 个警告 0 个错误
```

（首轮有一次 `CS0029`：`SendMessageTimeout` 返回 `IntPtr`，我写成了 `return ok;`，已修为 `ok != IntPtr.Zero`。）

## 2. 运行时观测（探针日志 `%TEMP%\wantodo-probe.log`）

### 2.1 本机桌面层拓扑

```
Progman=0x10172 DefView=0x10178 DefViewHost=0x10172(Progman) WorkerW(顶层)=23 候选挂载点=0x10172
SendMessageTimeout(Progman, 0x052C) → ok=True, result=0x0, lastErr=0
```

**关键发现**：在 Windows 11 26200 上，`SHELLDLL_DefView` 直接是 **Progman 的子窗口**，
顶层 `WorkerW` 有 23 个但**没有一个持有 DefView**；发 `0x052C` 前后拓扑完全一致（消息被接受但不产生新 WorkerW）。

推论（要在 verify 里说清楚，不要当成永久结论）：
- 经典「找带 DefView 的 WorkerW 并 SetParent 过去」的教程在本版本上无对象可挂。
- 本机实际生效的挂载点是 **Progman**，即 `LayerMode.WorkerWOwner` 与 `LayerMode.ProgmanOwner`
  在本机**行为等价**（候选挂载点回退到 Progman）。探针两种模式都能用来复测，但结论只对 Progman 成立。
- 因此 decisions.md D2 的顺序应改为：**先试 WorkerW，不存在就 Progman**；这不是"降级失败"，
  而是这一代 Windows 的正常形态。

### 2.2 挂载结果（`[STATE]`）

```
mode=WorkerWOwner hwnd=0x30BA2 visible=True owner=0x10172(Progman) TOPMOST=False TOOLWINDOW=False LAYERED=False
rect=240,240 1290x510 dpi=144 monitor=\\.\DISPLAY1 work=2560x1528 zIndex=64/605
```

外部独立核对（PowerShell + P/Invoke，不依赖探针自己的日志）：

```
visible=True  exstyle=0x0  owner=0x10172  GetParent=0x0
```

- `exstyle=0x0`：没有 `WS_EX_TOPMOST`、没有 `WS_EX_TOOLWINDOW`、**没有 `WS_EX_LAYERED`** —— 硬件加速保住了。
- `GetParent=0x0` + `owner=Progman`：确认是 **owner 关系**，不是子窗口。这是 D2 想要的效果。
- `rect=1290x510` 对 860×340 DIP，在 144 dpi 下正好 ×1.5，**PerMonitorV2 清单生效**。

### 2.3 Win+D

用户按 Win+D 后：探针窗口**仍在屏幕上**，日志里 `hideEvents=0`。

即：owner=Progman 的窗口不被 Win+D 隐藏，**不需要**任何"检测 WM_SHOWWINDOW(FALSE) 再抢回"的补丁。
探针里那套 `_autoReshow` 逻辑保留为开关（Ctrl+Alt+R），但本机不需要开启。

## 3. Explorer 重启

用 `Stop-Process -Name explorer -Force` 杀掉 explorer（自动重启为 pid 14032），探针收到 `TaskbarCreated`：

```
23:11:31.073 [====] 收到 TaskbarCreated —— Explorer 重启
23:11:31.456 [INFO] SetWindowLongPtr(GWLP_HWNDPARENT) ← 0x204C4 (Progman)   # 旧 Progman 是 0x10172
23:11:31.461 [STATE] visible=True owner=0x204C4(Progman) LAYERED=False zIndex=88/555
23:11:32.657 [INFO] SetWindowLongPtr(GWLP_HWNDPARENT) ← 0x204C4 (Progman)   # 1500ms 复查
23:11:35.201 [INFO] SetWindowLongPtr(GWLP_HWNDPARENT) ← 0x204C4 (Progman)   # 4000ms 复查
```

外部独立核对（重启后）：`visible=True exstyle=0x0 owner=0x204C4(新的 Progman)`。

结论：
- **新 Progman 是新句柄**（0x10172 → 0x204C4），旧 owner 失效后必须重新取拓扑并重挂；只重挂一次不够稳，
  300/1500/4000ms 三次幂等重挂是有效的（三次都成功，无副作用）。
- 重挂过程中窗口没有被销毁、没有变透明、没有丢 z 序层级问题。**不需要重建窗口**。
- 探针里 `rect` 从 `240,240` 变成 `735,84` 是用户手动拖动导致，不是程序移动。

## 4. 人肉验收（用户当场确认）

- [x] 探针窗口出现在**桌面图标层**（不是浮在所有窗口之上）
- [x] Win+D 显示桌面后仍在，`hideEvents=0`（说明 Shell 根本没尝试隐藏它）
- [x] 普通窗口（浏览器/资源管理器）能**盖住**探针
- [x] 拖动跟手，无残影
- [x] 文字清晰，无发虚（`exstyle=0x0`，确实没走分层窗口）
- [x] Explorer 重启后窗口自己回到桌面层
- [ ] DWM 材质（Acrylic/Mica）在桌面层窗口上是否生效 —— 未测，属 P1
- [ ] 多显示器 / 混合 DPI —— 本机只有一块屏（2560×1600 @150%），测不了，留到有第二块屏时补

## 5. 复跑方法

```powershell
# 1) 关掉旧实例（任务管理器或 Stop-Process DesktopLayerProbe）
# 2) 启动
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
& 'E:\Dev\Project\wantodo\spike\DesktopLayerProbe\bin\Debug\net10.0-windows\DesktopLayerProbe.exe'
# 3) 热键
#    Ctrl+Alt+1..5  WorkerW owner / Progman owner / Topmost / Normal / WorkerW+HWND_BOTTOM
#    Ctrl+Alt+D     把当前状态与 z 序 dump 进日志
#    Ctrl+Alt+R     自动抢回显示开关
#    Ctrl+Alt+Q     退出
# 4) 日志
Get-Content "$env:TEMP\wantodo-probe.log" -Encoding UTF8 -Tail 40
```

## 6. 结论

Phase 0 通过（除多显示器一项因硬件缺失无法测）。答案比预期简单：

1. **桌面层可挂**：`SetWindowLongPtr(GWLP_HWNDPARENT, Progman)` 即可。不需要 `SetParent`，
   不需要分层窗口，`exstyle=0x0` 硬件加速完好。
2. **Win+D 天然不受影响**，不需要任何"检测隐藏再抢回"的补丁。
3. 这一代 Windows（26200）**没有**可用的 WorkerW 壁纸层，产品代码要按 **Progman 优先、WorkerW 兜底** 写，
   而不是反过来。
4. Explorer 重启后只需重新取拓扑 + 重设 owner（三次幂等重挂），**不必重建窗口**；数据与 UI 状态零损失。

## 7. 给 Phase 1 的直接结论

- `Interop/DesktopLayer.cs` 里只需要三个能力：`FindProgman()`、`AttachToDesktopLayer(hwnd)`、
  `WatchShellRestart(hwnd)`（监听 `TaskbarCreated` → 重挂）。
- 探针里 `Topmost` / `Normal` / `HWND_BOTTOM` 三个对照模式**不进产品代码**，只留在 spike 里备查。
- `_autoReshow`（WM_SHOWWINDOW 抢回）**不进产品代码**：本机不存在被隐藏的场景，留着只是噪声。
- 探针的 `ShellTopology` 完整 dump 可以瘦身成：Progman 句柄 + 是否找到 WorkerW，只在启动和重挂时记 INFO。
