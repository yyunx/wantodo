# Phase 19 验证 — 面板 V2：比例尺寸与可拖缩放（M1）

日期：2026-11-14　HEAD：2dc868e（开工前）

## 1. 交付物

| 文件 | 内容 |
|---|---|
| `src/Wantodo.Core/PanelGeometry.cs`（新增，约 170 行） | 比例夹取、工作区 → 尺寸、尺寸 → 比例、边缘命中、拖拽夹取；纯函数 |
| `src/Wantodo.Core/Board.cs` | 新增 `PanelWidthRatio` / `PanelHeightRatio`（默认 0.75）；`NormalizePanel` 同时夹比例，旧 `PanelHeight` 只夹取不丢弃 |
| `src/Wantodo.Core/JsonBoardStore.cs` | 加载时改用 `board.NormalizePanel()` |
| `src/Wantodo.App/Windows/TopPanelWindow.xaml.cs` | 展开尺寸按工作区 × 比例；左右下边缘缩放；监听 `SystemParameters.WorkArea` 与 `DisplaySettingsChanged`；`Ctrl+N` 聚焦面板的快速添加 |
| `src/Wantodo.App/Windows/TopEdgePanelService.cs` | 钩子先判边缘再判把手，移动/松开分流到缩放或拖动 |
| `src/Wantodo.App/ViewModels/BoardViewModel.cs` | `SetPanelRatios(width, height)`（夹取 + 去抖写盘） |
| `src/Wantodo.App/MainWindow.xaml.cs` / `NotesWindow.xaml.cs` | `Ctrl+N`：打开随心记并弹「先起名字」 |
| `src/Wantodo.App/SettingsWindow.xaml` | 开关说明改「撞顶出把手；拖左/右/下边缘调整大小，尺寸按屏幕比例记忆」 |
| `tests/Wantodo.Core.Tests/PanelGeometryTests.cs`（新增，8 例） | 75% 换算（1366×728 / 2560×1560）、比例夹取、最小尺寸与 90% 上限、命中边、左边缘与下边缘拖拽、比例回算 |
| `tests/Wantodo.Core.Tests/JsonBoardStoreTests.cs`（+2 例） | 旧文件只有 `panelHeight`：比例补 0.75、旧高度保留、保存后两个新字段写入；非法比例夹到 0.4–1.0 |

## 2. 命令与输出

```text
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
    已成功生成。
    0 个警告
    0 个错误
    已用时间 00:00:09.21

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test --no-build
已通过! - 失败: 0，通过: 216，已跳过: 0，总计: 216，持续时间: 596 ms
```

（Phase 18 结束时 206 例；本阶段 +10 例。）

## 3. 实现要点

- 数据契约：`Board.PanelWidthRatio` / `PanelHeightRatio` 都是 double，默认 0.75，夹 0.4–1.0；
  `PanelHeight` 保留为旧文件回退字段（240–720），**不再驱动尺寸，也不会被保存时清空**。
- 展开尺寸：`PanelGeometry.ExpandedSize(WorkArea, wRatio, hRatio)`，最小 480×240、最大工作区 90%；
  `PlaceTopCenter` 水平居中，`Top = 工作区 Top`（任务栏在顶部时也跟着下移）。
- 缩放：命中带宽 6 DIP；顶边 12 DIP 一律返回 `None`，留给「往下拖展开」。
  `Resize` 只动被拖的那条边，对边锚定；左边缘拖动时 `Left` 与 `Width` 一起变，右边缘不动。
- 缩放结束后 `RatiosFromSize` 把 `ActualWidth/Height` 换算回比例，交给 `SetPanelRatios` 走既有的
  600 ms 去抖与原子落盘。
- `Ctrl+N`：主窗口 → 打开随心记并弹新建对话框；面板 → 清空并聚焦快速添加框。`Esc` 行为未改。

## 4. 与计划的偏差（重要）

计划写的是「`Interop/TopPanelSizing.cs` 里用 `WM_NCHITTEST` 返回 `HTLEFT/…` + `WM_GETMINMAXINFO`」。
实现改为**由 Phase 17 已存在的低级鼠标钩子驱动缩放**，原因：

1. 面板 `ShowActivated=false`，没有 `WS_THICKFRAME` 时 `WM_NCHITTEST` 的 `HT*` 不会触发系统缩放；
   要生效得手工加 `WS_THICKFRAME`，会改变客户区与非客户区，和现有 12 DIP 把手、DWM 圆角冲突。
2. 面板的「往下拖展开」本来就由钩子驱动，缩放走同一套命中与位移换算，行为一致、少一条未验证路径。
3. 缩放的最小/最大值在 `PanelGeometry.Resize` 里夹取，`WM_GETMINMAXINFO` 不再需要。

代价：不享受系统的边缘光标反馈（鼠标指针不会变成双向箭头）。记入欠账，若手感需要再补
「钩子移动时切换 `Cursor`」的小改动。

## 5. 验收对照（plan Phase 19 六条）

| # | 验收 | 结果 |
|---|---|---|
| 1 | 1366×768 展开 1025×546；2560×1600 展开 1920×1170，误差 ≤2 DIP | **PENDING-HUMAN**（工作区换算已有单测，实际屏幕尺寸需人眼/截图确认） |
| 2 | 拖右/下/右下角改大小；拖顶边 12 DIP 仍展开/收起；重启恢复比例 | **PENDING-HUMAN**（命中与夹取已有单测；手感与重启需人工） |
| 3 | 全屏视频/游戏自动收起并隐藏热区，退出恢复 | **PENDING-HUMAN**（沿用 Phase 17 watcher，本次未改） |
| 4 | 0.5.1 的 `data.json` 打开按 0.75，保存后新字段写入、旧 `PanelHeight` 不清空 | **AUTO 通过**：`Load_LegacyPanelHeightOnly_DefaultsRatiosAndKeepsHeight` |
| 5 | `Ctrl+N` 主窗口与面板都能新建；`Esc` 不回归 | **PENDING-HUMAN**（代码路径已接，需实际按键） |
| 6 | `build -warnaserror` 0 警告；`test` 全绿，新增 ≥4 例 | **AUTO 通过**：0 警告 0 错误；216 通过（+10） |

## 6. 需人肉眼确认（3–5 步）

1. 设置页勾「顶边下拉面板」→ 撞顶出把手 → 下拉展开。
2. 拖右边缘往左 200 px、拖下边缘往上 150 px，窗口应跟手变小，栏内文字不溢出。
3. 拖右下角斜着放大，最大不超过工作区 90%；拖顶边 12 DIP 应仍是「收起/展开」，不改变宽度。
4. 关掉开关再打开、退出重开 wantodo：展开尺寸应与上次一致（工作区 75%）。
5. 主窗口按 `Ctrl+N` 应弹出「先起名字」；面板内按 `Ctrl+N` 光标进快速添加框；`Esc` 仍收起面板。

期望：第 2–3 步窗口边缘跟手、无闪烁；第 4 步尺寸误差目测 ≤2 DIP。失败长什么样：拖边缘时窗口
只移动不变大、或松手后回到 75%、或宽度超过屏幕。

## 7. 已知限制

- 只处理主显示器；多显示器留未排期。
- 最大 90% 工作区，避免拖成无边框全屏后与热区/全屏判定打架。
- 缩放时没有系统的边缘光标（见 §4 偏差）；不影响功能。
- 工作区变化时若面板处于展开态，直接改成新比例尺寸，没有动画。
