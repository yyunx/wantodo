# Phase 6 验收：窗口尺寸跟随栏数（AutoFit / Manual）

- 日期：2026-11-10
- 目标：解决用户提的第 ① 条——「删第 3 栏后窗口应收回两栏宽度」，并把"窗口多宽"从隐式行为变成可配置、可解释的显式策略。

## 1. 做了什么

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.Core/LayoutMetrics.cs` | 新增。纯函数尺寸模型：`PreferredPaneWidth=280`、`MinPaneWidth=190`、`PaneGap=8`、`ChromeWidth=16`；`ComputePreferredWidth(n)=16+n×288`（1..4 栏 → 304/592/880/1168）；`ComputeMinWidth(n)=16+n×198`（214/412/610/808）；`FitTo(width,n,workArea)`；`ComputeMaxPanes(available)`；`SizeModes`（`AutoFit`/`Manual`）与 `Normalize` |
| `src/Wantodo.Core/WindowPlacement.cs` | 新增 `SizeMode`（默认 `AutoFit`），随 placement 一起落盘 |
| `src/Wantodo.Core/JsonBoardStore.cs` | `Normalize` 里把 `SizeMode` 归一化（未知值/缺字段/maximized 之类一律回到 `AutoFit`） |
| `src/Wantodo.App/MainWindow.xaml.cs` | `ApplyAutoFit(reason)`：按栏数算目标宽度、夹进工作区、右边越界时左移；`SetSizeMode(mode, reason)`；`OnWindowSizeChanged` 检测"用户手动改宽度"→ 自动转 `Manual`；`OnWindowStateChanged`（最大化/最小化）转 `Manual`；启动时 `ApplyAutoFit("启动")` 收敛一次 |
| `src/Wantodo.App/SettingsWindow.xaml(.cs)` | 新增「窗口尺寸：跟随栏数 / 固定」单选组，改动立即生效并回填 |
| `tests/Wantodo.Core.Tests/LayoutMetricsTests.cs` | 12 例（宽度公式、`FitTo` 四种夹取、`ComputeMaxPanes`、`SizeModes` 默认与归一化） |
| `tests/Wantodo.Core.Tests/JsonBoardStoreTests.cs` | +2 例（`SizeMode` 默认 `AutoFit` 与 `Manual` 往返；缺字段/非法值 → `AutoFit`） |

## 2. 命令与输出

```
dotnet build Wantodo.slnx -warnaserror
已成功生成。 0 个警告 0 个错误

dotnet test Wantodo.slnx
已通过! 失败: 0，通过: 83，已跳过: 0，总计: 83
```

## 3. 实测（真机，Win11 26200，2560×1600 @150%，工作区宽 1706 DIP）

测试数据：2 栏、`592×360`、`AutoFit`、`Desktop`；用户真实数据先备份到 `%TEMP%\phase6-userdata-backup.json`（3 栏：想做的事情 4 条 / 论文 2 条 / 想到的问题 1 条）。

| # | 操作 | 结果（日志原文） |
| --- | --- | --- |
| 1 | 加第 3 栏 | `AutoFit（加栏）：592 → 880 DIP（3 栏）@ 730` |
| 2 | 加第 4 栏 | `AutoFit（加栏）：880 → 1168 DIP（4 栏）@ 539`（右边贴近工作区，left 由 730 挪到 539） |
| 3 | 删一栏（4→3） | `AutoFit（删栏）：1168 → 880 DIP（3 栏）@ 539` |
| 4 | 再删一栏（3→2） | `AutoFit（删栏）：880 → 592 DIP（2 栏）@ 539` |
| 5 | 带着 4 栏、存的是 592 宽时启动 | `AutoFit（启动）：592 → 1168 DIP（4 栏）@ 539` |
| 6 | 手动把窗口拖到 900 px | `用户调整了窗口尺寸，切到 Manual（之后加栏/删栏不再自动改宽度）`，`data.json` 里 `sizeMode=Manual` |
| 7 | Manual 下加栏 | 宽度 `900 → 900 px` 不变 |
| 8 | 打开设置面板 | UIA 读到 `固定 selected=True`（自动切 Manual 已反映到 UI） |
| 9 | 设置里选「跟随栏数」 | `窗口尺寸策略切到 AutoFit（设置面板）` + `AutoFit（切回跟随栏数）：600 → 880 DIP（3 栏）@ 539`，JSON `sizeMode=AutoFit` |
| 10 | 还原用户数据后启动 | `AutoFit（启动）：601 → 880 DIP（3 栏）@ 730`（3 栏在 601 宽下会被压到最小宽度，启动即收敛到 880） |

截图：

- `docs/verify/phase6-settings.png` —— 设置面板多了「窗口尺寸」一行。
- `docs/verify/phase6-three.png` —— 3 栏、1320 px。
- `docs/verify/phase6-two.png` —— 2 栏、888 px（删栏后收回）。

## 4. 踩坑

1. **`SizeModes.Normalize` 第一版把 `"autofit"` 原样返回**，单测 `"autofit"` 期望 `"AutoFit"` 直接失败。改成"只认 Manual，其余一律返回常量 `AutoFit`"——归一化的输出必须是枚举里的那几个常量，不能是输入的回声。
2. **合成鼠标点击送不进 WPF**：桌面层与普通窗口状态下都试过，`WindowFromPoint` 确认命中我们的 hwnd，但按钮毫无反应（不报错、不触发）。改用 **UI Automation**：`InvokePattern` 点「＋栏」、栏头「✕」，`SelectionItemPattern.Select()` 切「跟随栏数」，`WindowPattern.Close()` 关设置窗——全部一次成功。以后交互验证优先 UIA。
3. **0,0 矩形的旧账**：上一轮曾因 `Get-Rect` 返回 `0,0` 时继续合成点击，误触任务栏。这次在 `%TEMP%\wantodo-ui.ps1` 里加 `Assert-Rect`，矩形非正直接抛错，脚本层面阻断。
4. **默认值语义**：`SizeMode` 缺省即 `AutoFit`，所以老数据（没有该字段）升级后会在启动时按当前栏数收敛一次尺寸——这是特性不是 bug，但要写进发布说明。

## 5. 已知限制

- 只按**栏数**算宽度，不表达"某栏特别宽"（等分模型）。等 Phase 9 的停靠布局引入 `ratio` 后才会有非等分。
- 工作区宽度是主屏工作区；多显示器时不会尝试落到副屏。
- AutoFit 只在 Normal 窗口态生效；最大化/最小化会把策略切成 Manual（这是刻意的，避免用户主动最大化后被我们改回去）。

## 6. 复跑

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
Get-Process Wantodo -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build Wantodo.slnx -warnaserror
dotnet test Wantodo.slnx
Start-Process .\src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe
Get-Content "$env:APPDATA\wantodo\app.log" -Encoding utf8 -Tail 20
```

## 7. 需要人肉眼确认的

1. 加栏时窗口变宽的那一下，是否"跳"得让你不舒服（目前是即时的，没有动画）。
2. 删栏收窄后窗口左边缘原地不动、右边缘向左收——这个方向对不对，还是你希望从左边收？
3. 手动改过宽度之后，再加栏不再变宽，是否符合直觉。
4. 设置面板里「跟随栏数 / 固定」的措辞是否够清楚（是否需要改成"自动撑开 / 保持我调的尺寸"）。
5. 3 栏在 880 DIP 下、4 栏在 1168 DIP 下，每栏内容的可读性是否可接受。
