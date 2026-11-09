# Phase 4 验收证据 — 托盘 / 设置 / 全局快捷键 / 主题

- 日期：2026-11-10
- 环境：Windows 11 26200，单屏 2560×1600 @150%，.NET SDK 10.0.403
- 结论：**通过**（4 条验收里 3 条已实测，1 条「注销重进」只能验证到注册表读写，见「待人工确认」）

## 1. 本阶段交付物

| 文件 | 作用 |
|---|---|
| `src/Wantodo.App/Shell/TrayIcon.cs` | `NotifyIcon` + 右键菜单（显示/隐藏、设置、层级、外观、隐藏已完成、开机自启、退出）；左键单击切换显示，左键双击开设置 |
| `src/Wantodo.App/Shell/Autostart.cs` | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 读写，写 `"<exe 路径>"`，关闭时删值 |
| `src/Wantodo.App/SettingsWindow.xaml(.cs)` | 无边框设置面板：层级 / 外观 / 色调强度 / 隐藏已完成 / 开机自启，改动立即生效 |
| `src/Wantodo.App/QuickCaptureWindow.xaml(.cs)` | `Ctrl+Alt+Space` 唤出的「快速记一条」输入框，回车写入当前栏、Esc 取消 |
| `src/Wantodo.App/Appearance/ThemeService.cs` | 把 `ThemePalette` 算出的 7 个颜色写进 `Application.Current.Resources`（画刷全部 Freeze） |
| `src/Wantodo.Core/ThemePalette.cs` | 纯函数调色：深/浅两套基色 + 色调强度混色，不依赖 WPF |
| `src/Wantodo.App/Assets/wantodo.ico` | 7 个尺寸（16/24/32/48/64/128/256）的 DIB ICO，372,526 字节 |
| `src/Wantodo.App/app.manifest` | `PerMonitorV2` + `longPathAware` |
| `src/Wantodo.App/Interop/NativeMethods.cs` | 追加 `RegisterHotKey` / `UnregisterHotKey` / `ShowWindow` / `ShowWithoutActivating` / `WM_HOTKEY` 常量 |
| `src/Wantodo.App/ViewModels/*.cs` | `IsHidden`（隐藏已完成）、`SetTheme` / `SetTone` / `HideCompleted` / `ApplyAppearance` |
| `tests/Wantodo.Core.Tests/ThemePaletteTests.cs` | 8 例：深浅基色、未知主题回退、色调夹取、NaN、`FromHex`/`ToHex`/`Lerp` |
| `tests/Wantodo.Core.Tests/JsonBoardStoreTests.cs` | 追加 2 例：`hideCompleted` 默认 false、往返、缺字段回退 false |

## 2. 命令输出

```
$ dotnet build Wantodo.slnx -warnaserror
已成功生成。
    0 个警告
    0 个错误

$ dotnet test Wantodo.slnx
已通过! - 失败: 0，通过: 54，已跳过: 0，总计: 54，持续时间: 455 ms
```

## 3. 验收项与实测证据

| 验收项 | 结论 | 证据 |
|---|---|---|
| 隐藏后托盘左键能召回 | ✅ | 日志 `托盘：隐藏窗口` → `IsWindowVisible=false`；再点一次 `挂到桌面层 host=0x5029E（Progman）` + `托盘：显示窗口（不夺焦点）`、`IsWindowVisible=true` |
| 召回不夺焦点 | ✅ | 召回后 `GetForegroundWindow()` 仍是 `explorer.exe` 的托盘溢出窗口，不是 `wantodo` |
| 切换层级立即生效并写配置 | ✅ | 设置面板点「普通窗口」：`GetWindow(GW_OWNER)=0x0`，`surface=Normal` 落盘；点回「桌面层」：`owner=0x5029E`，`surface=Desktop` |
| 自启开 → 注销重进后出现 | ⚠️ 只能验证到注册表 | 勾选后 `HKCU\...\Run` 出现 `"E:\Dev\Project\wantodo\...\Wantodo.exe"`；取消后该值被删除。真实注销重进未做（会把当前会话打断），见待人工确认 |
| `Ctrl+Alt+Space` → 输入 → 回车 → 主窗可见 | ✅ | 日志 `快速记一条（成功）→ 要做的事情：快速记一条V2`；`data.json` 栏 1 出现该条 |
| 主题切换 | ✅ | 点「浅色」后 `theme=Light` 落盘、界面变浅色；重启后仍为浅色（见下面的 bug 2） |
| 隐藏已完成 | ✅ | 勾选后 `hideCompleted=true`，已勾选的两条从界面消失但 `data.json` 里仍是 4 条；取消勾选后又出现 |
| 色调强度 | ✅ | `tone=0.55` 时窗口底色偏蓝、卡片仍白；`ThemePaletteTests` 覆盖夹取与 NaN |
| 版本号与图标 | ✅ | exe 属性 0.4.0.0，`Assets/wantodo.ico` 在资源管理器里显示为深底蓝勾 |

截图（同目录）：`phase4-settings.png`（设置面板）、`phase4-light.png`（浅色主窗）、`phase4-light-tone.png`（浅色 + 色调 0.55）、`phase4-hide-completed.png`（隐藏已完成生效，计数仍是 4）、`phase4-traymenu.png`（托盘右键菜单）、`phase4-quickcapture.png`（快速记一条）。

## 4. 本阶段踩到的坑

1. **PNG 压缩的 ICO 不能用 `System.Drawing.Icon` 读**：第一版 3589 字节的 PNG-in-ICO 一加载就 `请求的范围扩展超过了数组的结尾`。改成手写多尺寸 DIB ICO（`BITMAPINFOHEADER` + BGRA + AND mask），372,526 字节，7 个尺寸都能被 Explorer 正确显示。
2. **`UseWindowsForms` 会污染全局 using**：`Application` / `TextBox` / `Point` / `DragEventArgs` / `KeyEventArgs` / `MouseEventArgs` 同时存在于 WPF 与 WinForms，开 `UseWindowsForms` 后立刻 `CS0104` 大面积歧义。解法是 csproj 里 `<Using Remove="System.Windows.Forms" />` + `<Using Remove="System.Drawing" />`，需要时用 `Drawing` / `WinForms` 别名。
3. **`ApplicationManifest` 指向不存在的文件**：`CS2001 未能找到源文件`。补 `src/Wantodo.App/app.manifest`（PerMonitorV2）。
4. **真崩溃：快速记一条回车后进程退出**（事件日志 `System.InvalidOperationException: 在窗口关闭期间，无法将可见性设置为可见，也无法调用 Show、ShowDialog、Close...`，`Exception code: 0xc000041d`）。原因是 `Commit()` 里 `Close()` 触发 `Deactivated`，`Deactivated` 里又 `Close()`。修法：`QuickCaptureWindow` 加 `_closing` 标记 + `CloseOnce()`，三个入口全走它。顺带在 `App.OnStartup` 挂了 `DispatcherUnhandledException` / `AppDomain.UnhandledException`，以后这类异常至少会留在 `app.log` 里而不是静默消失。
5. **真 bug：主题重启后丢失**。设置面板改主题能立即生效，但重启后回到默认深色——启动路径里没人调 `ThemeService.Apply`。修法：`MainWindow` 构造函数里 `InitializeComponent()` 之后立刻 `ThemeService.Apply(board)`。实测重启（`theme=Light`、`tone=0.55`）后界面确实是浅色偏蓝。
6. **合成点击坐标两次打偏**：托盘图标的勾选圆点在物理像素 `1135,222`（UIA 报的 `BoundingRectangle`），我按截图目测的 `1122` 落在圆点左侧 13px 外，点了没反应还以为是 bug。教训：**先用 UI Automation 拿 `BoundingRectangle` 再点**，不要靠肉眼看缩放后的截图。

## 5. 与计划的偏离

| 计划里写的 | 实际做法 | 原因 |
|---|---|---|
| 托盘右键菜单含「栏数」 | 只保留 层级 / 外观 / 隐藏已完成 / 开机自启 / 设置 / 退出 | 加栏已经在主窗顶栏有「＋ 栏」，托盘里再放一份属于重复入口；栏数不是高频操作 |
| 设置面板含「栏数」 | 未做 | 同上；且「栏数」本质是「加一栏 / 删一栏」，做成数字选择器反而绕 |
| 设置面板含「透明度」 | 改成「色调强度」 | D3：WPF 的窗口透明度会触发分层窗口、关掉硬件加速，与常驻小组件冲突 |
| 托盘右键菜单列出「主题」 | 只有深色 / 浅色两项 | 「跟随系统」需要一个 `SystemEvents.UserPreferenceChanged` 订阅与三态存储，留到后续；现在两种主题已覆盖主需求 |

## 6. 已知限制

- WPF 默认样式的 `Slider` / `CheckBox` 在深色主题下仍是浅色控件外观（白轨道、白方块）。功能正常，观感待后续统一。
- 托盘图标默认在 Windows 11 的溢出区里，用户需要拖到任务栏常驻（系统行为，程序无法强制）。
- 「快速记一条」总是记到当前激活栏（`ActiveColumn`，没有则第一栏），不能先选栏。
- 自启写的是当前 exe 的绝对路径；如果用户移动了程序目录，需要重新开关一次。

## 7. 复跑命令

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
cd E:\Dev\Project\wantodo
dotnet build Wantodo.slnx -warnaserror
dotnet test Wantodo.slnx
Start-Process .\src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe
Get-Content "$env:APPDATA\wantodo\app.log" -Encoding utf8 -Tail 20
```

## 8. 待人工确认（我做不了的部分）

1. **注销重进**：勾上「开机自启」→ 注销 → 重新登录，确认 wantodo 自动出现；再关掉开关 → 注销重进，确认不再出现。（注册表读写已验证，登录时的启动链路没法在会话内自测。）
2. **托盘图标手动拖到任务栏**：设置 → 个性化 → 任务栏 → 其他系统托盘图标 → 把 wantodo 打开，确认常驻后左键单击的手感。
3. **置顶模式**：切到「置顶」后，确认它盖住浏览器但仍能被拖动、能编辑；再切回「桌面层」确认落回壁纸上。
4. **浅色主题在真实壁纸上的观感**：浅色卡片贴浅色壁纸时边界是否够清楚（我只在深色壁纸上截了图）。
5. **快速记一条的落点**：屏幕上方 16% 处是否符合你的习惯（多显示器下只在主屏出现）。
