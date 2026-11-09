# Phase 3 验收记录 — 健壮性（写盘节流 / 位置兜底 / 系统事件）

- 日期：2026-11-10
- 环境：Windows 11 26200，主屏 2560×1600 @150%（DIP 1706×1066，工作区底边 1018.67 DIP）
- SDK：10.0.403（用户目录安装，每次命令行都要设 `DOTNET_ROOT`）

## 1. 这一阶段改了什么

| 文件 | 改动 |
| --- | --- |
| `src/Wantodo.Core/PlacementGuard.cs` | 新增：`IsFullyInside(...)` + `Clamp(...)`，纯函数无 WPF 依赖 |
| `src/Wantodo.App/ViewModels/BoardViewModel.cs` | `SaveNow()` → `RequestSave()`（400ms `DispatcherTimer` 合并）+ 新增 `FlushSave()` 同步落盘；异常时把脏标记恢复 |
| `src/Wantodo.App/ViewModels/ColumnViewModel.cs` | 三处保存调用改成 `RequestSave()` |
| `src/Wantodo.App/MainWindow.xaml.cs` | 位置兜底改走 `PlacementGuard`（只在整个窗口跑出虚拟桌面时才介入，目标是**主屏工作区**，不会藏到任务栏底下）；挂 `SessionSwitch` / `DisplaySettingsChanged` / `PowerModeChanged`；重写 `OnDpiChanged`；`FlushPendingSave()` 供退出/关机调用 |
| `src/Wantodo.App/App.xaml.cs` | 启动计时（`首帧 …ms`）+ 内存/线程快照；`OnSessionEnding` 强制落盘 |
| `tests/Wantodo.Core.Tests/PlacementGuardTests.cs` | 9 个用例（`Clamp` 6 + `IsFullyInside` 3） |

## 2. 命令输出

```
dotnet build Wantodo.slnx -warnaserror
    0 个警告
    0 个错误

dotnet test Wantodo.slnx
已通过! - 失败: 0，通过: 44，已跳过: 0，总计: 44
```

## 3. 实测

### 3.1 启动耗时与内存（日志 `首帧 Nms；内存 WS=…MB private=…MB；线程=…`）

| 次 | 首帧 | WS | private | 线程 |
| --- | --- | --- | --- | --- |
| 1 | 1069ms | 122MB | 75MB | 19 |
| 2 | 805ms | 115MB | 73MB | 15 |
| 3 | 811ms | 115MB | 73MB | 15 |
| 4 | 801ms | 116MB | 74MB | 15 |
| 5 | 835ms | 116MB | 75MB | 15 |

- 首帧 = `OnStartup` 到 `ContentRendered`，含框架启动 + JIT，稳定在 0.8–1.1s。
- 运行一段时间后系统会裁剪工作集：实测 `WS=75.7MB private=85.9MB 线程=16`。
- **结论**：WS 不是基线（会被裁剪），private 才是；private 实测区间 **73–86MB**，没有随运行时间无限上涨。

### 3.2 位置兜底（`data.json` 里写 `left=5200, top=1400` 再启动）

```
[WARN] 保存的位置 5200,1400 在屏幕外，已拉回到 1106,658.6666666666666
[INFO] 窗口就绪 hwnd=0x370194 600.6666666666666×360 DIP @ 1106,658.6666666666666
```

- `1106 = 1706 - 600.67`，`658.67 = 1018.67 - 360`（工作区底边，不含任务栏）。
- 截图 `p3-clamped.png` 确认窗口完整可见、栏尾输入框没被任务栏盖住。
- **中途发现的真 bug**：最初用「至少露 80×40」的规则，结果窗口停在 `1626.67,1026.67`，几乎整个在屏幕外；第一版修成「拉进虚拟桌面」，又会被任务栏盖住。最终规则：**整个窗口不在虚拟桌面内 → 拉回主屏工作区**；用户自己把窗口卡在边缘不动它。

### 3.3 写盘节流 + 强杀

| 场景 | 操作 | 结果 |
| --- | --- | --- |
| 节流窗口内强杀 | 回车后 **168ms** 杀进程 | 界面已有该条（截图 `p3-instant.png` 显示 3 条），`data.json` 里没有，**JSON 仍合法** → 最多丢 0.4s 内的输入，不会损坏 |
| 越过节流后强杀 | 回车后等 900ms 再强杀 | `data.json` 已包含该条，重启后仍在 |
| 正常退出 | 回车后立刻点 ✕ | 日志 `已保存布局 …；内存 …`，`data.json` 包含刚输入的条目（`FlushPendingSave` 生效） |

### 3.4 Explorer 重启回归

```
[WARN] 收到 TaskbarCreated：Explorer 重启了，准备重挂
[INFO] 挂到桌面层 host=0x5029E（Progman）；visible=True owner=0x5029E topmost=False layered=False
[INFO] Explorer 重启后重挂 成功；visible=True owner=0x5029E topmost=False layered=False
```

- 窗口句柄不变（`hwnd=0x44……`），位置不变，Phase 1 的能力在 Phase 3 改动后没有回归。

## 4. 未能实机验证的项（代码已实现，请按下面的方法手动确认）

1. **改分辨率 / 拔副屏**：`ClampIntoVirtualScreen` 与 `DisplaySettingsChanged` 分支。理论上与 3.2 走同一条 `PlacementGuard` 代码路径，但 `WM_DISPLAYCHANGE` 回调本身没被触发过。
   - 手动测法：设置 → 系统 → 显示 → 分辨率改成 1280×800，观察 `app.log` 是否出现 `显示设置变化：重挂桌面层 …`，窗口是否仍在屏内。
2. **改缩放（DPI）**：`OnDpiChanged` 会重算栏宽。需要注销重登才能改全局缩放，没在这次里做。
3. **锁屏解锁 / 休眠唤醒**：`SessionSwitch`、`PowerModeChanged` 回调。
   - 手动测法：`Win+L` 解锁后看日志有没有 `会话切换：…`；合盖再打开看有没有 `电源恢复：…`。

> 这三项都属于「不打扰用户就没法测」的范畴。如果日志里出现了异常（例如重挂失败），说明代码路径有问题，回来再修。

## 5. 已知限制

- `private` 内存 73–86MB，比 Phase 1 记录的 69.7MB 略高（多挂了三个系统事件 + 计时器），仍在可接受范围；如果继续涨要查是不是事件没退订。
- 节流固定 400ms，没有做「用户连续输入时延长」的自适应。
- 强杀仍会丢最近 400ms 的输入——这是取舍，不是缺陷；正常关闭不丢。

## 6. 复跑命令

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet build E:\Dev\Project\wantodo\Wantodo.slnx -warnaserror
dotnet test  E:\Dev\Project\wantodo\Wantodo.slnx
Get-Content "$env:APPDATA\wantodo\app.log" -Encoding utf8 -Tail 20
```

## 7. 人肉确认清单

- [ ] 启动时组件出现在屏幕内（试一次把 `data.json` 的 `placement.left` 改成 5200）
- [ ] 加一条马上按 ✕ 关闭，重开还在
- [ ] `Win+L` 解锁后组件仍在桌面层、位置没变（看日志）
- [ ] 改分辨率后组件没跑丢
- [ ] 任务管理器里常驻内存是否可接受
