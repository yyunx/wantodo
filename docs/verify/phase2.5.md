# Phase 2.5 验收记录 —— 布局优化

- 日期：2026-11-09（跑到 00:0x，跨零点）
- 结论：**通过**
- 背景：用户反馈「增加栏目的时候不是一定得在右边加，然后最后越来越窄」

## 1. 改了什么

| 问题 | 改法 |
|---|---|
| 新栏永远追加到最右边 | 记住「最近操作过的那一栏」（键盘焦点或鼠标点进该栏卡片都算），新栏插在它**右边**；没有当前栏时才追加到末尾 |
| 栏数一多就越来越窄 | 每栏最窄 `190 DIP`，窗口放不下就横向滚动；加栏时先把窗口撑到「每栏 280 DIP」，撑到屏幕边界为止，撑不下才退到最小宽度 + 滚动条 |
| 新栏标题不统一 | 统一用 `BoardEditor.DefaultColumnTitle`（`未命名栏`） |

改动文件：

- `src/Wantodo.Core/BoardEditor.cs`：新增 `TryAddColumn(board, title, index, out column)` 重载（越界自动夹到 `0..Count`），原两参重载转发到它
- `src/Wantodo.App/ViewModels/BoardViewModel.cs`：`ActiveColumn` / `SetActiveColumn`；`AddColumn()` 按当前栏计算插入下标
- `src/Wantodo.App/MainWindow.xaml`：栏容器从 `UniformGrid` 换成 `ScrollViewer + ItemsControl + 横向 StackPanel`，栏卡片 `Width` 绑定窗口的 `ColumnWidth`
- `src/Wantodo.App/MainWindow.xaml.cs`：`ColumnWidth` 依赖属性、`UpdateColumnWidths()`（按可用宽度平均分，下限 190）、`GrowWindowForColumns()`（加栏时撑窗口）、`OnColumnPreviewMouseDown` / `OnColumnGotKeyboardFocus`（记录当前栏）
- `tests/Wantodo.Core.Tests/BoardEditorTests.cs`：新增 3 个用例（按下标插入 / 越界夹取 / 到上限仍拒绝）

## 2. 命令输出

```
dotnet build Wantodo.slnx -warnaserror
    0 个警告
    0 个错误

dotnet test Wantodo.slnx
已通过! - 失败: 0，通过: 35，已跳过: 0，总计: 35
```

## 3. 实测

| # | 操作 | 结果 |
|---|---|---|
| 1 | 2 栏、窗口 600 DIP 宽 | 每栏 ~284 DIP，等分 |
| 2 | 点第 1 栏空白处后点「＋ 栏」 | 新栏插在第 1 栏**右边**，`data.json` 顺序 `要做的事情 → 未命名栏 → 突然想到的问题` |
| 3 | 同上再点两次 | 栏数 4，窗口从 601 DIP 撑到 1160 DIP（`加栏后窗口加宽 601 → 1160 DIP`），每栏 280 DIP，无滚动条 |
| 4 | 手动把窗口缩到 780×520 px | 每栏保持 190 DIP（285 px），底部出现横向滚动条，文字不再被压成一条缝 |
| 5 | 到 4 栏后再点「＋ 栏」 | 按钮置灰，加栏被拒绝 |

截图：

- `docs/verify/phase2.5-window.png`：默认两栏（桌面上）
- `docs/verify/phase2.5-four.png`：4 栏、窗口自动撑宽、每栏 280 DIP
- `docs/verify/phase2.5-narrow.png`：窗口缩窄后每栏保底 190 DIP + 横向滚动条

## 4. 踩坑

1. `System.Text.Json` 之外，WPF 侧也有一个坑：`ItemsControl` 的容器宽度不能靠 `DataTemplate` 里写死，否则所有栏共享一个值；改成绑定 `Window.ColumnWidth` 依赖属性，由窗口统一算。
2. 一开始用 `Math.Clamp(ColumnWidth, ...)` 当加栏目标宽度，结果窗口本来就是窄的、`ColumnWidth` 已经被夹到 190，加栏时只撑到 800 DIP。改成固定目标 `280 DIP`，才真正「加一栏撑一栏」。
3. 「当前栏」不能只依赖键盘焦点：用户可能只是点一下栏的名字。补了 `PreviewMouseLeftButtonDown`。

## 5. 已知限制

- 横向滚动条是 WPF 默认样式，比较粗，和整体风格不搭（Phase 4 做外观时统一）
- 窗口自动撑宽只往「右边界内」调整，不会跨屏；多屏时以 `SystemParameters.WorkArea`（主屏）为准，副屏场景留到 Phase 3
- 加栏导致的窗口变宽不会即时落盘，要等关窗时保存（和 Phase 1 的行为一致）

## 6. 人肉确认清单

- [ ] 4 栏时每栏 280 DIP 的宽度是否舒服（还是想更宽/更窄）
- [ ] 加栏时窗口自动变宽，会不会觉得「窗口自己动了」很突然
- [ ] 窗口缩窄后的横向滚动条是否能接受
