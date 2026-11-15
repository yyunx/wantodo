# v6 块 B — 画板卡片行为（图片去框 / 待办选栏 / 计时消歧 / 番茄计划）

日期：2026-11-15　块：B　依赖：块 A（已提交 `7c09d01`）

## 1. 交付物

| 变更 | 文件 | 要点 |
|---|---|---|
| 图片卡去框 | `src/Wantodo.App/Controls/CanvasBoard.cs` | 无背景、无描边、无标题栏；图片铺满圆角裁剪；悬停才出标题浮层与 ×；双击/右键打开原图；选中时才 1 px 高光 |
| 待办选栏目 | `src/Wantodo.App/Controls/CanvasBoard.cs`、`src/Wantodo.App/ViewModels/BoardViewModel.cs`、`src/Wantodo.App/Windows/TopPanelWindow.xaml.cs` | 下拉列出全部栏目 + 「＋ 新建栏目」；新建复用 `ColumnNameDialog` 与 `AddColumnAndGetId` |
| 每面可见性 | `src/Wantodo.Core/Board.cs`、`src/Wantodo.App/Controls/BoardLayoutHost.cs` | `DesktopVisibleColumnIds` / `PanelVisibleColumnIds`（空 = 全部）；面板按 `PanelVisibleColumnIds` 过滤布局 |
| 计时消歧 | `src/Wantodo.App/Dialogs/TimerSetupDialog.xaml`、`src/Wantodo.App/Controls/CanvasBoard.cs` | 弹窗按钮改「设置完成」，只保存不启动；卡片按钮「开始 / 暂停 / 继续」 |
| 番茄计划 | `src/Wantodo.Core/PomodoroPlan.cs` | `PomodoroSegment`（Repeat/Focus/Break）+ `PomodoroPlan.Timeline()` 纯函数 |
| 暂停累计 | `src/Wantodo.Core/CanvasClock.cs` | `AccumulatedSeconds` 与带 `baseElapsed` 的 `DisplaySeconds` / `IsFinished` 重载 |

## 2. 命令与输出

```
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -warnaserror
    0 个警告
    0 个错误

& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test --no-build
已通过! - 失败: 0，通过: 274，已跳过: 0，总计: 274，持续时间: 486 ms
```

新增测试：

- `tests/Wantodo.Core.Tests/PomodoroPlanTests.cs`：默认 25/5/4 时间轴（7 项、末组无休息）、自定义 50/10×2 + 25/5×2 秒序、`Repeat=0` 丢弃回退、`TotalGroups` 夹取 1–64。
- `tests/Wantodo.Core.Tests/BoardVisibilityTests.cs`：空列表 = 全显示；桌面/面板独立；未知 id 忽略。
- `tests/Wantodo.Core.Tests/CanvasClockTests.cs`：暂停基数、累计封顶、完成判定。

## 3. 手动验收（需人肉眼确认）

1. 画板插一张图片卡：应当没有边框/标题栏，图片铺满圆角；鼠标移上去才出现半透明标题条与 ×；双击打开原图。
2. 插一张待办卡：点栏目下拉，选已有栏目后正文预览出现该栏最近条目；选「＋ 新建栏目」应弹命名框，确定后下拉里出现新栏且已选中。
3. 插一张计时卡：先用工具条「计时」配置 1 分钟，弹窗按钮是「设置完成」；确定后卡片停在 `00:00` 不自动跑；点「开始」才走，再点「暂停」数字停住，点「继续」接着走。
4. 到点：右下角出现一次提醒、响一声；卡片显示 `00:00`，不会重复弹第二次。

期望：以上 4 条都不需要重启程序。
失败长什么样：图片卡仍有可见方框/标题栏；待办下拉只有「（未选择栏）」；点「设置完成」后计时直接开始；到点连响多次。

## 4. 已知欠账

- 桌面/面板「各显示哪些栏目」目前只有数据字段与面板过滤逻辑，**还没有设置 UI**；面板只在 `PanelVisibleColumnIds` 非空时过滤，默认为空（全显示）。
- 面板过滤视图里拖动分隔条/栏头不持久化（`BoardLayoutHost` 在有过滤时直接 return）。
- 图片卡旧的 `BuildImageBody` 代码已无调用，未删除。
- 删除卡片仍未进 `trash/`；50 张卡性能未测；关程序期间的计时不补提醒。
- 番茄钟「自定义分段」已有 Core 计划模型与时间轴，设置 UI 尚未接入（当前计时卡仍是单段倒计时/番茄）。

## 5. 下一步第一条命令

块 C：主题体系重构（7 套色板 + 旧 id 迁移 + 语义令牌 + 形式层 `flat`）。
