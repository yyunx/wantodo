# Phase 1 验收记录 — 骨架 + 两栏界面

- 日期：2026-11-09
- 机器：Windows 11 26200，单屏 2560×1600，DPI 150%（144 dpi）
- SDK：.NET SDK 10.0.403（`%LOCALAPPDATA%\Microsoft\dotnet`，非管理员）
- 结论：**通过**（三条需要人眼确认的项见文末，已在本机观察过）

## 1. 交付物

```
Wantodo.slnx
src/Wantodo.Core/            Board.cs NoteColumn.cs Note.cs WindowPlacement.cs JsonBoardStore.cs
src/Wantodo.App/             App.xaml(.cs) MainWindow.xaml(.cs) AppLog.cs
src/Wantodo.App/Interop/     NativeMethods.cs DesktopHost.cs DesktopLayer.cs ShellRestartWatcher.cs
tests/Wantodo.Core.Tests/    JsonBoardStoreTests.cs（12 个测试）
```

与 Phase 0 探针的差异：产品只搬了三项能力（`DesktopHost.Find`、`DesktopLayer.Attach`、
`ShellRestartWatcher`）。**没有搬** `Topmost`/`Normal`/`HWND_BOTTOM` 对照热键、`WM_SHOWWINDOW`
抢回逻辑、完整拓扑 dump。三档层级在 `DesktopLayer.Attach` 里保留（`Desktop`/`Normal`/`Topmost`），
由 `data.json` 的 `surface` 字段选择，默认 `Desktop`。

## 2. 构建与测试

```
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet build Wantodo.slnx -warnaserror
→ 已成功生成。0 个警告 0 个错误

dotnet test tests\Wantodo.Core.Tests\Wantodo.Core.Tests.csproj
→ 已通过! 失败: 0，通过: 12，已跳过: 0，总计: 12
```

测试覆盖：首次无文件 → seed 两栏；首次保存不产生 bak、不留 tmp；第二次保存旧内容进 bak；
全字段往返（含 `Completed`、`Placement`、`Surface`、`Tone`）；主文件损坏 → 回退 bak 并隔离；
主文件缺失但 bak 存在 → 回退；空文件算损坏；主备都坏 → 起 seed 且两个都隔离；字段缺失补默认；
未知字段忽略；恢复后保存能重建主文件；`DefaultDataPath` 落在 `%APPDATA%\wantodo`。

## 3. 运行时证据（`%APPDATA%\wantodo\app.log`）

首次启动：

```
[INFO] 启动 data=...\wantodo\data.json status=FirstRun columns=2 surface=Desktop
[INFO] 首次启动，默认落点 1098.6666666666667,48
[INFO] 窗口就绪 hwnd=0x40B9E 560×320 DIP @ 1098.6666666666665,48
[INFO] DWM 圆角：已请求
[INFO] 挂到桌面层 host=0x204C4（Progman）；visible=False owner=0x204C4 topmost=False layered=False
[INFO] 已监听 TaskbarCreated (0xC066)：Explorer 重启后会自动重挂桌面层
```

（`visible=False` 是挂载时刻还没 `Show()`，随后外部枚举确认 `visible=True`。）

| 验收点 | 证据 |
|---|---|
| 挂到桌面层 | 外部 `EnumWindows` 核对：`hwnd=0x40B9E visible=True rect=1099,48,1659,368 owner=0x204C4(Progman)`；`GetParent()=0`（是 owner 不是子窗口） |
| 没退化成分层窗口 | `layered=False topmost=False`；`exstyle` 里没有 `WS_EX_LAYERED` |
| DPI | 560×320 DIP → 840×480 px（×1.5），`GetDpiForWindow=144`，PerMonitorV2 生效 |
| 两栏渲染 | `docs/verify/phase1-window.png`（PrintWindow 抓的真实窗口内容） |
| Win+D | 前后 `IsWindowVisible=True`，`rect` 不变（1099,48,1659,368）→ 不需要任何抢回补丁 |
| 浏览器能盖住 | 用户桌面实测：浏览器最大化时窗口在后面（Phase 0 已确认，Phase 1 行为一致） |
| Explorer 重启 | `Stop-Process -Name explorer -Force` 后：`Progman 0x204C4 → 0x30710`，收到 `TaskbarCreated`，300/1500/4000ms 三次重挂全部成功，`visible=True`，窗口未重建 |
| 布局持久化 | 关窗保存 `560×320 @ 1098.67,48`；重启日志 `status=Loaded`、`@ 1098.6666666666665,48`（没有「首次启动」行） |
| 用户拖动/缩放后持久化 | 实测期间用户把窗口拖到 `730,80` 并放大到 `600.67×360`，退出时写回 `data.json`，重启位置尺寸一致 |
| 数据驱动（不是硬编码两栏） | 手工把 `data.json` 里 `"title": "要做的事情"` 改成 `"今天要做"`，重启后 UI 标题变为「今天要做」 |
| 原子写 | 落盘后目录里只有 `data.json` / `data.bak`，无 `.tmp` 残留；第二次保存时旧内容进 `data.bak` |
| 无 `AllowsTransparency` | `grep -r AllowsTransparency src` → 只有 `MainWindow.xaml:8` 的显式 `AllowsTransparency="False"` |
| P/Invoke 只在一个出口 | `grep -r DllImport src` 排除 `Interop\` 后为空 |

启动耗时与内存（`Get-Process`）：

```
刚启动、静止 8 秒：working set = 113.1 MB, private = 69.7 MB
用了约 4 分钟（含用户拖动、缩放、Explorer 重启、多次截图抓取）后：177.5 / 117.4 MB
```

第一次测到的 177.5 MB 是在窗口已被拖动/缩放、Explorer 重启过一次之后。刚启动的干净实例是
113.1 MB 工作集 / 69.7 MB 私有，**私有内存已在 80 MB 以内，工作集略超**。
再闲置约 2 分钟后系统把工作集裁到 **22.9 MB（私有仍 69.7 MB、14 线程）**——说明前面的
工作集数字是峰值而不是常驻基线。真正的结论要看「私有无增长 + 长期挂机工作集曲线」，
这两条记进 Phase 3 的验收项。

## 4. 踩到的问题（已修）

1. **`double.NaN` 不能进 JSON**：`WindowPlacement.Left/Top` 原本用 `NaN` 表示「没存过位置」，
   序列化直接抛 `ArgumentException: .NET number values such as positive and negative infinity cannot be written`。
   改成 `double?`（`null` = 没存过），`HasPosition` 由 `Left.HasValue && Top.HasValue` 派生。
2. **`HasPosition` 被写进 JSON**：派生字段跟着序列化成 `"hasPosition": true`，用户手改文件时会困惑。
   加 `[JsonIgnore]`。
3. **中文被转义成 `\u8981\u505A...`**：默认 encoder 会转义非 ASCII，用户没法手改。
   改用 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`，`data.json` 里直接是中文。
4. **PowerShell 5.1 读 UTF-8 无 BOM 日志会乱码**：`Get-Content` 默认按 ANSI 解码。
   看日志要 `Get-Content ... -Encoding utf8`。这不是程序 bug，是验证时的坑。
5. **截窗口内容要 DPI 感知**：非 DPI-aware 的 PowerShell 里 `GetWindowRect` 返回 DIP 坐标，
   按它建位图再用 `PrintWindow` 只会抓到左上角一块（表现为「右侧那一栏被裁掉」，其实是采集错位）。
   要么先 `SetProcessDPIAware()`，要么按 `GetDpiForWindow` 换算成物理像素。

## 5. 已知限制（不属于 Phase 1 范围）

- **还不能编辑**：没有新增/编辑/勾选/拖动排序，这是 Phase 2。
- **没有托盘**：关窗即退出；标题栏右侧的 `✕` 是 Phase 1 的临时关窗入口，Phase 4 换成托盘菜单。
- **圆角没生效**：`DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)` 返回成功，但截图里窗口仍是方角
  —— 对 `WindowStyle="None"` 的窗口这条属性不改变形状。Phase 4 做外观时再定：自绘圆角、加 1px 描边，或接受方角。
- **多显示器 / 混合 DPI / 锁屏唤醒**：本机单屏，未测。启动时有屏幕外位置纠正（`ClampIntoVirtualScreen`），
  但拔插显示器的动态场景留到 Phase 3。
- **只读视图不滚动也不换行溢出**：条目多了靠 `ScrollViewer` 滚动，Phase 2 再定高度策略。

## 6. 复跑

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
cd E:\Dev\Project\wantodo
dotnet build Wantodo.slnx -warnaserror
dotnet test tests\Wantodo.Core.Tests\Wantodo.Core.Tests.csproj
Start-Process src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.App.exe
Get-Content "$env:APPDATA\wantodo\app.log" -Encoding utf8 -Tail 10
```

## 7. 需要人肉确认的项（已由本会话观察，但手感类仍请你自己过一遍）

- [x] 拖标题栏跟手、不抖
- [x] 拖窗口边缘能缩放（右下角/四边），缩放后内容不糊
- [x] 150% 缩放下文字清晰、不发虚（`textFormattingMode=Display`）
- [x] 按 Win+D 后窗口还在桌面上
- [x] 浏览器最大化能盖住它（不抢镜）
- [ ] **手感**：拖拽时有没有拖影/延迟？缩放时有没有闪烁？—— 机器没法判断，请你自己拖两下
- [ ] **外观**：方角 + 深色是否可接受？还是 Phase 4 要先补圆角
