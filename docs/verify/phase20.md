# Phase 20 验证 — 形态 A+：共享布局宿主，栏数 > 4（M2）

日期：2026-11-14　HEAD：2b8b9bf（开工前）

## 1. 交付物

| 文件 | 内容 |
|---|---|
| `src/Wantodo.App/Controls/BoardLayoutHost.cs`（新增，约 270 行） | 从 `MainWindow.xaml.cs` 原样搬入 `RebuildLayout` 的渲染路径、`BuildLayoutElement`、`OnSplitterDragCompleted`、`ApplySplitLengths`、`SplitView`；对外 `Attach(Board, Columns)`、`PaneTemplate`、`SaveRequested`、`MinSize`、`Rebuild()`、`FitToViewport(w,h)` |
| `src/Wantodo.App/MainWindow.xaml` | 根加 `xmlns:controls`；`Grid` → `controls:BoardLayoutHost` |
| `src/Wantodo.App/MainWindow.xaml.cs` | 改为宿主装配 + `SaveRequested` 转发；删除本地的布局渲染与拖拽实现（−210 行） |
| `src/Wantodo.App/Windows/TopPanelWindow.xaml` | 竖排 `ItemsControl` 换成 `ScrollViewer` + `controls:BoardLayoutHost`；新增紧凑的 `PanelPaneTemplate`（标题 + 备注切换按钮） |
| `src/Wantodo.App/Windows/TopPanelWindow.xaml.cs` | 装配宿主、订阅栏集合变化与视口尺寸、`FitHost()`；首次显示前 `Rebuild()` |
| `tests/Wantodo.Core.Tests/LayoutTreeTests.cs`（+4 例） | `Balanced(5/6/8)` 深度 ≤ `MaxDepth` 且 `Validate` 通过；`SplitterSize == 6` 且宽于顶边 2 DIP |

## 2. 命令与输出

```text
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
    0 个警告
    0 个错误

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test --no-build
已通过! - 失败: 0，通过: 220，已跳过: 0，总计: 220，持续时间: 603 ms
```

（Phase 19 结束时 216 例；本阶段 +4 例。）

启动回归（Debug，`DOTNET_ROOT` 指向用户目录）：

```text
PID 11284 启动成功
2026-11-14 22:51:20.020 [INFO] 首帧 1532ms；内存 WS=132MB private=74MB；线程=14
```

无异常、无绑定错误；默认关闭顶边面板时日志无「顶边下拉面板已启用」，宿主未创建（验收第 4 条）。

## 3. 实现要点

- 宿主只做搬运：`BuildLayoutElement` 的 `Grid`/`GridSplitter`/比例、`SplitView` 记录、拖完
  `LayoutTree.SetRatio` 后 `SaveRequested` 全部与原实现一致；差异只在资源查找改成
  `TryFindResource("PaneTemplate")` / `TryFindResource("Accent")`，因为宿主不在 `MainWindow` 的
  命名范围里。
- 面板叶子模板更紧凑：栏头 + 一条「备注」切换按钮（`NotesWindow` 的完整行模板在 760 宽面板里
  放不下）；点击行为与桌面一致（`ToggleCommand`）。
- 放不下就滚：`FitToViewport` 用 `LayoutTree.ComputeMinSize(Layout)` 撑最小尺寸，
  `ScrollViewer` 横纵都 `Auto`；栏不会被压成缝。
- 同步：面板与主窗口共用同一个 `BoardViewModel` 和同一棵 `LayoutNode`；面板拉分隔条 →
  `SaveRequested` → `RequestSave`，主窗口下次 `Rebuild()` 读到新比例。桌面 → 面板方向靠
  面板打开时 `Rebuild()` 与 `Columns.CollectionChanged`。
- 深度：`Balanced(5/6/8)` 实测深度均 ≤ 4，`MaxDepth` 保持 4，不改上限（计划第 4 条
  「先测、超限才提到 6」）。

## 4. 偏差与欠账

- **面板内没有「拖栏/拖条目」**：主窗口的拖拽预览、停靠区高亮、`PaneDataFormat` 处理没有搬进面板；
  面板目前只支持「点条目切换完成」与「拉分隔条」。验收第 1 条里「面板拖栏」与第 3 条的
  「栏头 > 条目」优先级目前不成立，记为欠账，留到 0.7.0 面板 V2 之后统一做。
- 「面板不响应拖到顶边」自然成立（没有拖拽入口），但没有专门的命中测试。
- 横向滚动条是否在 1366×768 × 5 栏时出现，未肉眼验证（`FitToViewport` 逻辑上会撑宽，
  但滚动条可见性取决于模板测量）。

## 5. 需人肉眼确认

1. **主窗口回归**：主窗口加一栏 → 拖分隔条 → 删一栏 → 拖栏头换个位置。期望与 0.5.1 一致，
   分隔条能拖、栏不重叠、重启后比例保留；失败形态：拖不动、栏宽跳变、重启比例回默认。
2. **5 栏横向滚动**：把窗口压到约 1366×768，加到 5 栏，开顶边面板。期望面板底部出现横向滚动条，
   每栏不被压到 190 DIP 以下；失败形态：栏被压成细缝、或内容被裁掉且无滚动条。
3. **双向同步**：桌面拖分隔条 → 撞顶开面板看比例；面板拉分隔条 → 收起再开桌面看比例。
   期望 1 秒内一致；失败形态：两边比例不同、或一边重启后丢失。
4. **开关关闭**：设置里关掉顶边面板 → 重启。期望撞顶无任何反应、日志无「已启用」；
   失败形态：仍有热区或面板。

## 6. 已知限制

- `GridSplitter` 的拖动仍走 WPF 自己的鼠标捕获（宿主在可激活的主窗口/面板里，没有 Phase 17
  那个「未激活窗口收不到 MouseMove」的问题）。
- 面板宿主的高度由 `PanelHeightRatio` 决定，栏内内容超出时面板内部纵向滚动，不改变面板高度。
- 没有新增第三方依赖；`BoardLayoutHost` 只用 WPF 自带控件。

## 7. 交付后补丁（同日，用户实测反馈）

用户反馈：面板「每个栏目之间没有分割，也拖不了；大小可以动，位置不行」。根因有两层：

1. 分隔条 `Background` 是透明，平时看不到缝；而且面板 `ShowActivated=false`，`GridSplitter`
   的 WPF 鼠标捕获在未激活窗口上不可靠——和 Phase 17 的把手是同一个坑。
2. 面板里根本没有「拖动整栏换位」的入口。

修法（全部走已有的全局鼠标钩子，不依赖窗口激活）：

- `BoardLayoutHost`：分隔条平时用 `Divider` 画刷画一条 6 DIP 淡分隔线，悬停变 `Accent`；
  新增 `TryBeginSplitDrag/DragSplit/EndSplitDrag`（命中分隔条 → 按屏幕位移改两列/两行的像素宽，
  松手回算比例并 `SaveRequested`）；新增 `TryBeginPaneDrag/DragPane/EndPaneDrag`
  （栏头 26 DIP 为拖动区，拖到另一栏松手 → `LayoutTree.SwapPanes` + 重建 + 保存）。
- `TopPanelWindow` 暴露同名转发；`TopEdgePanelService` 的按下顺序改为
  **边缘缩放 > 12 DIP 把手 > 分隔条 > 栏头拖动 > 下拉放行 > 点外收起**，移动/松开按当前手势分流。
- 栏内文字点击不受影响：拖动区只在栏头顶部 26 DIP，条目按钮仍走原来的 `ToggleCommand`。

证据：

```text
build -warnaserror：0 个警告，0 个错误
test --no-build：220 通过 / 0 失败 / 611 ms
启动 Debug：PID 39176，无异常
```

需人肉眼确认（替换第 5 节第 2 条的一部分）：

1. 开面板，看三栏之间是否有淡色竖缝 → 按住竖缝左右拖 → 栏宽跟手、松手后比例保留。
2. 按住某栏标题（栏内最上方）拖到另一栏上松手 → 两栏位置互换，重启后保持。
3. 点栏内条目 → 仍正常切换完成态，不会误触发拖动。

剩余欠账：面板内仍不支持「拖条目到另一栏」（笔记拖放）；它和主窗口的 `PaneDataFormat`
拖放逻辑共用一套，留到 0.7.0 一起做。

## 8. 下一步

Phase 21（主题形式感与樱花）——Run B 的第一阶段。
