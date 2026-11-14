# Phase 24 验收：形态 B 自由画布（MVP）

日期：2026-11-14。结论：**MVP 通过构建与单测；画布窗口的手工交互项待肉眼确认**。

## 1. 交付物

| 文件 | 类型 | 说明 |
|---|---|---|
| `src/Wantodo.Core/CanvasItem.cs` | 新增 | `CanvasItem`（id/kind/x/y/w/h/z/style/payload）与 `CanvasDocument`（Normalize、NextId、Add） |
| `src/Wantodo.Core/CanvasStore.cs` | 新增 | `board/canvas.json` 原子读写；主文件坏 → `.bak` → 隔离后空画布 |
| `src/Wantodo.App/Controls/CanvasBoard.cs` | 新增 | 卡片渲染、拖动、右下角缩放、8 DIP 吸附、置顶、撤销/重做、删除 |
| `src/Wantodo.App/Windows/CanvasPanelWindow.xaml(.cs)` | 新增 | 画布窗口：5 个工具按钮、Esc/收起、Ctrl+Z/Y、Delete |
| `src/Wantodo.Core/Board.cs` | 修改 | `PanelMode`（`Dock`/`Canvas`，默认 `Dock`），`NormalizePanel()` 未知值回退 Dock |
| `src/Wantodo.Core/DataPaths.cs` | 修改 | `CanvasFile => board/canvas.json` |
| `src/Wantodo.App/ViewModels/BoardViewModel.cs` | 修改 | `PanelMode` 属性与 `SetPanelMode()`，切换即保存 |
| `src/Wantodo.App/SettingsWindow.xaml(.cs)` | 修改 | 「面板形态」单选：桌面版（默认）/ 画板版 |
| `src/Wantodo.App/Windows/TopEdgePanelService.cs` | 修改 | 撞顶后按 `PanelMode` 选择下拉面板或画布窗口 |
| `tests/Wantodo.Core.Tests/CanvasStoreTests.cs` | 新增 | 9 例：夹取、去重、Add 的 z 递增、存读往返、缺失/损坏文件、备份、PanelMode 回退、两份文件互不覆盖 |

## 2. 命令与输出

```
$ dotnet build -warnaserror
已成功生成。
    0 个警告
    0 个错误

$ dotnet test --no-build
已通过! - 失败: 0，通过: 239，已跳过: 0，总计: 239
```

冒烟：启动 Debug 构建 PID 37376，日志「顶边下拉面板已启用（主屏顶边 2 DIP）」「首帧 1336ms；WS=134MB private=104MB；线程=14」，无异常。

## 3. 实现要点

- 数据隔离：画布写 `board/canvas.json`，板数据仍写 `board/data.json`；单测 `BoardAndCanvas_UseSeparateFiles` 验证两份文件互不覆盖。
- 卡片位置用 `Canvas.Left/Top`，尺寸改 `Border.Width/Height`；全程 DIP，150% 缩放下不混用物理像素。
- 撤销栈存 `canvas.json` 的 JSON 快照，上限 20 步；重做栈随新操作清空。
- 删除当前是「直接从文档移除 + 可撤销」，**尚未写进 `trash/`**（见欠账）。

## 4. 与计划的偏差

1. **`Controls/CanvasHost` 命名为 `CanvasBoard`**，职责相同；没有单独的宿主控件层。
2. **删除未进回收站**：撤销可恢复，但重启后不能从 `trash/` 找回。计划第 4 条只完成一半。
3. **卡片内容只显示 kind 摘要**（计时秒数、链接、图片路径），记事/待办卡片还没有和真实 `Note`/`NoteColumn` 双向绑定。
4. **未做 50 张卡片的帧率采样**（计划验收第 2 条），`CompositionTarget.Rendering` 采样脚本未写。

## 5. 需要人肉眼确认

1. 设置 → 面板形态切到「画板版」→ 鼠标撞屏幕顶部 120 ms：应出现画布窗口（不是下拉面板）。
2. 依次点 5 个工具：5 张卡片应插在视口中心附近；拖动卡片应吸附到 8 DIP 网格。
3. 拖右下角小方块：宽高应在 140×90 到 640×640 之间变化；关闭再撞顶，位置尺寸应保持。
4. Ctrl+Z 撤销最近一次拖动，Ctrl+Y 恢复；点卡片右上角 × 删除后 Ctrl+Z 应还原。
5. 切回「桌面版」再撞顶：应回到原来的下拉面板，栏位布局不变。

失败形态：撞顶出的是旧面板、卡片拖不动只跟手一半、重启后卡片回到左上角、切回桌面版后布局变空。

## 6. 欠账

- 删除进 `trash/`（可恢复）未做。
- 记事/待办卡片与真实笔记、栏目的绑定未做（当前只是占位摘要）。
- 50 张卡片性能采样未做。
- 画布缩放（zoom）、多显示器未做（设计文档已列入未排期）。
