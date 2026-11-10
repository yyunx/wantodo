# Phase 9 验收记录 — 停靠布局（任意位置摆放 + 栏内缩放）

日期：2026-11-10
结论：**通过**（拖拽停靠的「鼠标手感」需人肉眼确认，见文末清单）

## 1. 交付物

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.Core/LayoutNode.cs` | `DockSide`、`LayoutNode`（单类 + `type` 判别：`leaf` / `split`）、`LayoutTree`（`Balanced` / `LeafIds` / `InsertBeside` / `MovePane` / `RemovePane` / `SwapPanes` / `FindSplit` / `SetRatio` / `Validate` / `ComputePreferredSize` / `ComputeMinSize`） |
| `src/Wantodo.Core/Board.cs` | 新增 `LayoutNode? Layout` 与 `string? NormalizeLayout()`；数据 `Version` 升到 2 |
| `src/Wantodo.Core/JsonBoardStore.cs` | 读盘时校验并归一布局，非法则回退均衡横向树并把原因写进 diagnostics |
| `src/Wantodo.Core/BoardEditor.cs` | 加栏按「插到当前栏右侧」同步改树；删栏后让树坍缩 |
| `src/Wantodo.App/MainWindow.xaml` | `ScrollViewer` 内换成 `Grid x:Name="LayoutHost"`；栏模板抽成 `DataTemplate x:Key="PaneTemplate"`；栏头挂 `PreviewMouseMove` / `MouseRightButtonUp`，栏卡挂 `AllowDrop` / `DragOver` / `DragLeave` / `Drop` |
| `src/Wantodo.App/MainWindow.xaml.cs` | `RebuildLayout()` / `BuildLayoutElement()`（递归生成嵌套 `Grid` + `GridSplitter`）、`OnSplitterDragCompleted`（按实际像素回算 ratio）、`UpdateColumnsHostSize()`、`UpdateColumnWidths()` 改为树驱动、停靠拖拽与右键菜单 |
| `src/Wantodo.App/ViewModels/BoardViewModel.cs` | `MovePane` / `SwapPanes`（先改树、校验通过才落盘） |
| `src/Wantodo.App/ViewModels/ColumnViewModel.cs` | `public string Id => Model.Id;`（布局树按数据层 id 找栏） |
| `tests/Wantodo.Core.Tests/LayoutTreeTests.cs` | 20 例：均衡建树、四向插入、跨层移动、删除坍缩、互换、比例夹取、结构校验、尺寸推算、v1→v2、竖直树 JSON 往返 |

## 2. 命令

```
dotnet build Wantodo.slnx -warnaserror   → 0 警告 0 错误
dotnet test                             → 150 通过 0 失败
```

## 3. 实测

| # | 做了什么 | 结果 |
| --- | --- | --- |
| 1 | 默认 2 栏启动 | 均衡横向树 `(a|b)`，与旧版观感一致 |
| 2 | 点「＋ 栏」两次 | 4 栏等宽，树变成 `((a|b)|(c|d))`，每栏 280 DIP，窗口 880 → 1168 DIP |
| 3 | 手工把 `data.json` 的 layout 改成 `Vertical 0.4`（上：想做的事情；下：论文｜想到的问题） | 启动日志 `绘制布局：根=split/Vertical 叶=3`，界面正是「上一条横栏 + 下两栏」，重启后保持 |
| 4 | 拖动第一根分隔条向左 84px | `layout.first.ratio` 0.5 → 0.40188，日志 `分隔条 0：比例 0.40`；**窗口宽度不变**（1168 DIP） |
| 5 | 加栏后的 AutoFit | 窗口 880 → 1168 DIP，`placement.width` 同步写成 1168、`left` 538.67 |
| 6 | 加删栏位次 | 加栏插到当前栏右侧；删栏后树坍缩，不留下空位 |
| 7 | 栏头右键 | 菜单「移到最左 / 最右 / 最上 / 最下 / 与右边一栏互换」可用（UIA 可点） |

截图：`docs/verify/phase9-four.png`（4 栏）、`docs/verify/phase9-vertical.png`（竖直嵌套）。

## 4. 这一轮修掉的真 bug

1. **`GridSplitter` 看得见、点不到**：分隔条没有内容，默认模板量出 0×0，UIA 的 `BoundingRectangle` 是 ±∞。修法是显式 `Stretch` + `Width/Height = SplitterSize`（6 DIP，实际 9px @150%）。
2. **AutoFit 只改窗口不落盘**：加栏把窗口撑到 1168，但 `placement.width` 还是 880，重启会跳回旧宽度。修法是 AutoFit 之后补一次 `SavePlacement`。
3. **布局树与栏列表不一致时静默回退**：`Validate` 发现叶子集合 ≠ 栏集合就换成均衡横向树，原来只写 diagnostics。现在回退会写 `AppLog.Warn`，能看见原因（排查「竖直树被打回横向」那次就是靠它定位的：当时 `data.json` 有 4 个叶子，栏只剩 3 个）。
4. **计算属性污染 JSON**：`isLeaf` / `isHorizontal` 被序列化进 `data.json`，给它们加 `[JsonIgnore]`，叶节点上的 `paneId` 用 `WhenWritingNull`。

## 5. 踩坑

- **`DoDragDrop` 收不到合成鼠标**：桌面层窗口 + OLE 拖放要在真实输入队列里跑，`mouse_event` 拖完日志一片空白；右键菜单同理（合成点击打不开）。所以停靠拖拽只能人肉验，代码里给了右键菜单当等价出口。
- **XAML 结构性改造不能用 PowerShell 行切片**：两次把内外层 `ItemsControl.ItemTemplate` 认错、切片越界，产出 297 行的坏文件。改用 Python 按「标签文本 + 缩进」定位并断言，一次成功。
- **`JsonBoardStore.Load()` 返回 `BoardLoadResult`**，要 `.Board` 才是 `Board`（`CS1061`）。
- **调试版与安装版共用 `Local\Wantodo.SingleInstance`**：测 Debug 前必须 `Get-Process Wantodo | Stop-Process -Force`，否则新进程直接退出、看起来像「代码没生效」。
- **桌面层窗口的 UIA 定位**：`RootElement.FindFirst(Children, pid)` 找不到（owner 是 Progman），要用 `AutomationElement.FromHandle(hwnd)`。

## 6. 已知限制

- 停靠只有 5 个落点（左/右/上/下/中间互换），没有自由浮动窗口，也没有「拖到窗口外单独成窗」。
- 树深上限 3 层、比例 0.15–0.85、每栏最小 190×120；超限由 `Validate` 拒绝而不是硬塞。
- 拖拽时只有栏头高亮 + 落点提示色，没有半透明预览块。
- 高度也参与竖向比例，但窗口高度仍由用户拖动决定（AutoFit 只管宽度）。

## 7. 复跑

```
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
Get-Process Wantodo -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build Wantodo.slnx -warnaserror
dotnet test
Start-Process 'src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe'
Get-Content "$env:USERPROFILE\..\..\E:\Wantodo\logs\app-*.log" -Encoding utf8 | Select-String '绘制布局|分隔条'
```

## 8. 需要人肉眼确认

1. 按住栏标题拖到另一栏的左/右/上/下边缘，松手后是否落在预期位置（四向 + 中心互换）。
2. 拖动分隔条时是否有明显延迟或跳动。
3. 竖直布局下，上栏高度 40% 是否观感合适（可再拖）。
4. 栏头右键菜单在你的鼠标上是否好点。
5. 3–4 栏时每栏内容是否仍可读（最小 190 DIP）。

## 增补：拖动条与落点预览（2026-11-10 19:56）

用户反馈「直接拖栏头不好抓」「拖到『卡住』按钮没反应」，于是：

- 栏卡片顶部中间加一条 38×4 的圆角横杠（Cursor="SizeAll"），按住它才能拖整栏；栏头仍然可拖，作为兜底。
- 拖到某一栏上时，DropPreviewLayer 上浮出半透明色块（#336FA8FF + 强调色描边），提前显示「将要占用的那半边」；中心区域表示两栏互换。
- 「卡住」按钮补 AllowDrop + DragOver/Drop：此前只接了窗口内部，按钮本身不是投放目标，所以拖上去没反应（真 bug）。
- 落点判定抽成 ComputeDropSide(point, width, height, out swap)，DragOver 与 Drop 共用同一套阈值（左右 28%/≤80px、上下 28%/≤60px）。

合成鼠标驱动不了 OLE 拖放，**这两条仍需人肉确认**：拖横杠换位、把条目拖到「卡住」按钮。
