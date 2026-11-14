# 0.6.0 执行断点（Run A / Run B）

新的会话先读本文件：从「下一个未 DONE 的阶段」继续。
基线（2026-11-14，HEAD 2dc868e）：`build -warnaserror` 0 警告 0 错误（15.23s）；
`test --no-build` 206 通过 / 0 失败 / 608 ms。

## Phase 17 — DONE
- run: A
- commit: 857d770（代码与 verify）、edaec8e（0.5.1 三处修复）
- 验收: 6/6（docs/verify/phase17.md；五条交互验收用户 2026-11-14 确认）
- 改动: 19 个文件（+1071/−32，857d770）
- 欠账 / PENDING-HUMAN: 「展开增量 private ≤6 MB」的原始数值留到 Phase 23 统一复测。
- 下一步第一条命令: 进入 Phase 18 回归（只跑 build/test + 关键路径）。

## Phase 18 — DONE
- run: A
- commit: a9e9f47（实现）、8c0145d（肉眼验收）
- 验收: 3/3（docs/verify/phase18.md；两条肉眼项用户 2026-11-14 确认）
- 改动: 9 个文件（+275/−6）
- 欠账 / PENDING-HUMAN: 本次只做回归：`test` 206 通过；「新建先命名 / 列表悬停显示文件名」两条
  由用户按 docs/verify/phase18.md §4 再点一遍，属确认性质，不阻塞。
- 下一步第一条命令: 进入 Phase 19（先加 `PanelWidthRatio` 单测，再动 UI）。

## Phase 19 — DONE
- run: A
- commit: 2b8b9bf
- 验收: 2/6 自动通过（build/test、旧 panelHeight 兼容）；4 条须人肉眼（docs/verify/phase19.md）
- 改动: 15 个文件（+665/−26）
- 欠账 / PENDING-HUMAN: 系统边缘光标反馈（改用钩子驱动缩放，无 `WM_NCHITTEST`）；四条肉眼项未验。
- 下一步第一条命令: 进入 Phase 20（先抽 `Controls/BoardLayoutHost`，主窗口切过去后再接面板）。

## Phase 20 — DONE-WITH-DEBT
- run: A
- commit: `git log --grep '^phase-20:' -1`（本阶段提交）
- 验收: 2/5 自动通过（关闭开关不建宿主、build/test 220 通过）；3 条须人肉眼（docs/verify/phase20.md）
- 改动: 6 个文件（+136/−264，另新增 BoardLayoutHost.cs）
- 欠账 / PENDING-HUMAN: 面板内「拖栏/拖条目」未搬入（主窗口拖拽预览与 PaneDataFormat 未进宿主）；
  分隔条命中优先级只有 `SplitterSize=6` 常量的单测；5 栏横向滚动与双向同步待肉眼。
- 下一步第一条命令: Run A 收口（§9 汇总，停；不进 Run B）。

## 下一步
Run A 收口：汇总表、自决清单、人工清单、预算对比，然后停。
