# 设计：停靠式自由布局（Dock Layout）

> 对应 Phase 9。目标：像 Obsidian 那样，把任意一栏停靠到窗口的任意位置（预设上/下/左/右 + 拖拽热区）；
> 停靠后窗口整体尺寸自适应；停靠完成后可以拖动分隔条单独调整每栏比例，**窗口外框保持原样**。

## 1. 交互语汇

| 术语 | 含义 |
|---|---|
| Pane（栏） | 一个便签栏（标题 + 条目列表），是布局的最小可见单元 |
| Split（分割容器） | 一个方向（水平/垂直）上的两路切分，带比例 `ratio` |
| Leaf（叶节点） | 指向某个 pane 的叶子 |
| Ratio | 分割容器里第一路的占比，`0.15 ≤ ratio ≤ 0.85` |
| Dock Zone（停靠热区） | 拖动 pane 时高亮的 5 个落点：上 / 下 / 左 / 右 / 中心（中心 = 替换/交换） |
| Splitter（分隔条） | 相邻两路之间的 6 px 拖拽条，拖动只改 ratio，不改窗口尺寸 |

## 2. 数据模型（Board schema v2）

```jsonc
{
  "version": 2,
  "panes": [
    { "id": "p-todo", "title": "要做的事情", "notes": [ /* … */ ] },
    { "id": "p-idea", "title": "突然想到的问题", "notes": [ /* … */ ] }
  ],
  "layout": {
    "type": "split",
    "orientation": "Horizontal",
    "ratio": 0.5,
    "first":  { "type": "leaf", "paneId": "p-todo" },
    "second": { "type": "leaf", "paneId": "p-idea" }
  }
}
```

C# 侧：

```csharp
public abstract record LayoutNode;
public sealed record LeafNode(string PaneId) : LayoutNode;
public sealed record SplitNode(
    Orientation Orientation,   // Horizontal = 左右切；Vertical = 上下切
    double Ratio,              // 0.15–0.85
    LayoutNode First,
    LayoutNode Second) : LayoutNode;
```

**不变量**（每次修改后 `Validate()` 必须通过）：

1. 每个 `paneId` 在树中恰好出现一次；树中所有 `paneId` 必须存在于 `panes[]`，反之亦然。
2. `ratio ∈ [0.15, 0.85]`，`double.IsFinite`。
3. 深度 ≤ 3（最多 8 个叶，实际受栏数上限约束）。
4. 每个 pane 渲染尺寸 ≥ 190 × 120（DIP）；窗口最小尺寸 = 树的最小尺寸。

## 3. 不可变操作（放 Core，纯函数，全部单测）

```csharp
LayoutNode InsertBeside(LayoutNode tree, string targetPaneId, string newPaneId, DockSide side);
LayoutNode MovePane(LayoutNode tree, string paneId, string targetPaneId, DockSide side);
LayoutNode RemovePane(LayoutNode tree, string paneId);      // 折叠空出来的 Split
LayoutNode SetRatio(LayoutNode tree, string splitPath, double ratio);
(double Width, double Height) ComputePreferredSize(LayoutNode tree, Func<string,(double W,double H)> panePref);
(double Width, double Height) ComputeMinSize(LayoutNode tree);
```

- `DockSide`：`Left / Right / Top / Bottom`；映射为「新建 Split，旧叶与新叶按方向排列」。
- `RemovePane`：叶被移除后，其父 Split 用兄弟节点替换自己（树坍缩），再自底向上夹取 ratio。
- `splitPath`：从根到目标 Split 的路径（`"0.1"` 表示根的第一路再第一路），用于分隔条拖动回写。

**尺寸推算**（用于窗口 AutoFit）：

```
Span(Horizontal, a, b).Width  = W(a) + W(b) + SplitterSize
Span(Horizontal, a, b).Height = max(H(a), H(b))
Span(Vertical,   a, b).Height = H(a) + H(b) + SplitterSize
Span(Vertical,   a, b).Width  = max(W(a), W(b))
Leaf(pane).W/H                = pane 偏好尺寸（默认 280 × 320）
```

## 4. 渲染

- **结构变更时重建**：从 `layout` 递归生成 `Grid`：
  - Horizontal Split → 3 列：`星号(ratio*)` / `6px Auto` / `星号((1-ratio)*)`；`GridSplitter` 放中间列。
  - Vertical Split → 3 行，同理。
- **比例变更时只改尺寸**：分隔条 `DragCompleted` → `ratio = 列1.ActualWidth / (列1 + 列2)` → 写回模型 →
  只更新对应 `ColumnDefinition.Width`，**不重建视觉树**（避免闪烁与焦点丢失）。
- 每栏外边距与卡片样式沿用现有 `Border`（圆角 10、`DynamicResource` 主题画刷）。
- 分隔条：6 px 透明/半透明，hover 时显示强调色；`ShowsPreview=false`（实时预览会卡）。

## 5. 拖拽交互（状态机）

```
Idle
 └─ (Pane 标题栏 MouseLeftButtonDown, 位移 > 4px) → DraggingPane
      ├─ 悬停进入某 Pane 的 4 个边缘带（各占 25%）或中心 25% → 更新 DockOverlay
      ├─ 悬停窗口四边（距边 < 24px） → 整体边缘新增（等价于对该方向最外层 Split 插入）
      └─ 松手 → ApplyDock(side) → 重建 → 持久化 → Idle
```

- **与条目拖拽共存**：条目拖拽用 `DataObject` 格式 `wantodo/note`，Pane 拖拽用 `wantodo/pane`；
  `DragOver` 时按格式分流，互不干扰。
- **视觉反馈**：`Adorner` 画半透明强调色块（如「左半区」），中心区画边框表示替换；
  拖拽期间被拖的 Pane 半透明（`Opacity=0.5`，非分层窗口，不影响渲染路径）。
- **等价预设**：右键盘标题 →「移动到 → 上 / 下 / 左 / 右」，走同一个 `MovePane` API。
  （无障碍与「手抖拖不准」的兜底。）
- **取消**：`Esc` 或拖回原位 → 不变更。

## 6. 窗口尺寸策略（与 Phase 6 的 AutoFit 统一）

| 触发 | 行为 |
|---|---|
| 加栏 / 停靠 / 删除栏 | 若 `sizeMode = AutoFit`：按 `ComputePreferredSize` 重算窗口尺寸（夹到工作区，保持右/下边缘不跳动） |
| 拖动分隔条 | **不改窗口尺寸**（满足「缩放时整体窗口原样」），只改 pane 比例 |
| 用户拖窗口边框 / 最大化 | `sizeMode = Manual`，之后结构变更不再自动改尺寸 |
| DPI 变化 | 重建 Grid，比例不变；按新 DPI 重算最小尺寸 |

- `sizeMode` 持久化在 `placement.sizeMode`（`AutoFit` / `Manual`），重启保持。
- 结构变更导致窗口必须放大但工作区放不下 → 夹到工作区上限，并允许 pane 缩到 190×120，
  再不够则整体出现滚动（复用 Phase 2.5 的横向滚动策略）。

## 7. 迁移（v1 → v2）

- `columns[]` → `panes[]`（字段 `id/title/notes` 原样保留，`id` 不变）。
- 生成均衡横向树：`Split(Split(Split(p1, p2), p3), p4)`，ratio = `1/n`（逐层取 `count/2` 再归一）。
- 写回 `version: 2`；旧版本程序读到 v2 会拒绝启动（提示升级），避免降级写坏布局。
- 单测：1/2/3/4 栏迁移后 `Validate()` 通过、叶子顺序与原栏顺序一致、内容逐字段相等。

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| 嵌套 `GridSplitter` 在 DPI 切换后比例漂移 | 重建时按 ratio 重新分配星号；拖动结束按 `ActualWidth` 重算 |
| 拖拽与条目拖拽冲突 | 两种 `DataObject` 格式分流；标题栏仅触发 Pane 拖拽 |
| 树被写坏（外部手改 JSON） | 启动时 `Validate()`，非法则回退 `.bak`，再不行降级为均衡布局并告警 |
| 停靠后窗口跑到屏幕外 | 复用 `PlacementGuard` 夹取 |
| 比例抖动/小于最小尺寸 | 夹取 + `MinWidth/MinHeight` 双保险 |

## 9. 测试矩阵

- Core 单测：插入 4 个方位、移动（含跨层）、移除后坍缩、比例夹取、尺寸推算、最小尺寸、非法树拒绝、v1 迁移。
- 手测：拖到右/下/上/左、中心替换、预设菜单、分隔条拖动（窗口不变）、删栏后收窄、重启恢复、
  4 栏 2×2 场景、DPI 150% 与 100% 切换、Explorer 重启后布局不变。
