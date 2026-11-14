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

## 下一步
Phase 19 — 面板 V2：比例尺寸与可拖缩放。
