# 0.6.0 执行断点（Run A / Run B）

> 2026-11-15 v6：本次做 **块 A → 块 B → 块 C → 块 D**（画板高度修复 → 卡片行为 → 主题体系 → 形式层）。

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
- 欠账 / PENDING-HUMAN: 面板内「拖条目到另一栏」未做；分隔条可见 + 拖分隔条 + 栏头换位已补（同日补丁，
  `docs/verify/phase20.md` §7）；5 栏横向滚动与双向同步待肉眼。
- 下一步第一条命令: Run A 收口（§9 汇总，停；不进 Run B）。

## Phase 21 — DONE-WITH-PENDING
- run: B
- commit: git log --grep '^phase-21:' -1（本阶段提交）
- 验收: 2/5 自动通过（樱花对比度单测、无新增图片）；3 条须人肉眼（docs/verify/phase21.md：关效果、DPI 截图、切换耗时）
- 改动: 12 个文件（+207/−17，新增 Appearance/ThemeStyles.xaml）
- 欠账 / PENDING-HUMAN: 描边宽度固定 1 px（StrokeWidth 字段已算）；面板/对话框的形式感留 Phase 22。
- 下一步第一条命令: 进入 Phase 22（补赛博/美漫预设，把 ThemeStrokeWidth 接到 Thickness 资源）。

## Phase 22 — DONE-WITH-DEBT
- run: B
- commit: git log --grep '^phase-22:' -1（本阶段提交）
- 验收: 1/4 自动通过（8 套对比度 230 通过）；其余肉眼/待 Phase 23
- 改动: 7 个文件（+62/−11，新增 docs/verify/phase22.md）
- 欠账 / PENDING-HUMAN: 面板/设置窗/取色器/对话框未接 ThemeStyle；外发光与硬阴影未绘制。
- 下一步第一条命令: 进入 Phase 23（先出两种包，再测首帧/内存/面板增量）。

## Phase 23 — DONE-WITH-DEBT
- run: B
- commit: git log --grep '^phase-23:' -1（本阶段提交）
- 验收: 2/4 自动通过（两种包字节、首帧 1236 ms）；private 100.0 MB 超预算 2 MB；面板增量未测
- 改动: 版本 0.6.0、release-0.6.0.md、verify/phase23.md、README、两个包
- 欠账 / PENDING-HUMAN: private +8 MB 归因；面板展开增量；Phase 22 遗留的面板/对话框形式感
- 下一步第一条命令: Phase 24（0.7.0 画布）——预估 3–5 天，触发「单阶段 >120 分钟」硬停，Run B 到此收口。

## 下一步
Run B 收口：21/22/23 完成（各有欠账），24 未开工（3–5 天，超出单次 Run 预算）。

## Run B — Phase 24（2026-11-14）

- 状态：**DONE-WITH-DEBT（MVP）**
- commit：本轮提交 `phase-24: 自由画布 MVP`
- 验收：`build -warnaserror` 0 警告/0 错误；`test` 239 通过；设置页「面板形态」与撞顶切画布已接；手工人肉眼项 5 条，见 `docs/verify/phase24.md` §5
- 改动：新增 Core `CanvasItem`/`CanvasStore`、App `CanvasBoard`/`CanvasPanelWindow`、`CanvasStoreTests`（9 例）；`Board.PanelMode`、`DataPaths.CanvasFile`、`BoardViewModel.SetPanelMode`、设置页单选、`TopEdgePanelService` 分流
- 欠账：删除未进 `trash/`；50 张卡片性能未采样；画布 zoom/多显示器未做
- 2026-11-15 补丁（未单独 commit，并入 phase-24 文档）：标题可改、链接内嵌记事/待办、图片选择/拖入/粘贴、
  计时四玩法 + 右下角提醒一次 + 提示音；`TextLinks`/`CanvasClock` 纯逻辑 + 2 组测试；`build` 0 警告、`test` 261 通过
- 下一条命令：等用户肉眼验收 `docs/verify/phase24.md` §11 五步；通过后再决定是否把删除进回收与 50 张性能补上

## 块 A — 画板高度拖拽可逆（2026-11-15）

- 状态：**DONE（PENDING-HUMAN 3 条）**
- 验收：`build -warnaserror` 0 警告/0 错误（6.88s）；`test --no-build` **265 通过 / 0 失败 / 534 ms**
- 改动：`PanelGeometry`（MinRatio 0.2、MaxRatio 0.9、ClampHeight、HeightRatio）、`TopPanelWindow`（绝对 Y 定高、EndDrag 持久化、唯一夹取入口、`[画板高度]` 日志）、`PanelGeometryTests` +4 / 改 2、`JsonBoardStoreTests` 夹取断言
- 证据：`docs/verify/block-a.md`
- 欠账：拖拽日志待用户实际拖一次补录（`persisted` 必须为 0）；多显示器未验证
- 并行修复：`E:\Wantodo\board\data.json` 的 `panelHeightRatio` 0.4 → 0.75（备份 `data.json.bak-block-a`）
- 下一步第一条命令：进入块 B（图片卡去框 → 待办选栏目 → 计时按钮消歧 → 番茄钟 plan）

## 块 B — 画板卡片行为（2026-11-15）
- 状态：**DONE（PENDING-HUMAN 4 条）**
- 验收：`build -warnaserror` 0 警告/0 错误；`test --no-build` **274 通过 / 0 失败 / 486 ms**
- 改动：`CanvasBoard`（图片卡去框 + 悬停浮层 + 双击打开；待办栏目下拉 + 「＋ 新建栏目」；计时 开始/暂停/继续 状态机）、`Board` 每面可见性字段、`BoardLayoutHost` 面板过滤、`PomodoroPlan`、`CanvasClock` 暂停基数重载
- 证据：`docs/verify/block-b.md`
- 欠账：可见性设置 UI 未做；面板过滤视图不持久化调宽/换位；图片卡旧 `BuildImageBody` 未删；删除未进回收；番茄自定义分段 UI 未接
- 下一步第一条命令：进入块 C（7 套色板 + 旧 id 迁移 + 语义令牌 + 形式层 flat）