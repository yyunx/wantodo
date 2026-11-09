# wantodo

贴在 Windows 桌面上的便签板 —— 像手机主屏的小组件，默认两栏：「要做的事情」「突然想到的问题」。

## 现状

**Phase 0 已通过**（2026-11-09）：桌面层挂载方式已在本机实测确认，证据见
[`docs/verify/phase0.md`](docs/verify/phase0.md)。下一步是 Phase 1（骨架 + 两栏界面），
贴 [`docs/prompts.md`](docs/prompts.md) 里的「2.5 Phase 1 实现」。

一句话结论：`SetWindowLongPtr(GWLP_HWNDPARENT, Progman)` 设 owner 即可贴在桌面图标层，
Win+D 不受影响，不需要分层窗口（`exstyle=0x0`，硬件加速完好）。这一代 Windows 没有可用的
WorkerW 壁纸层，所以宿主按 **Progman 优先、WorkerW 兜底**探测。

## 文档

| 文件 | 作用 |
|---|---|
| [AGENTS.md](AGENTS.md) | 给 Codex 的仓库约定（技术栈、硬性规则、完成定义） |
| [docs/spec.md](docs/spec.md) | 产品规格：场景、功能分级、数据模型、交互细节 |
| [docs/plan.md](docs/plan.md) | 分阶段计划 + 每阶段验收清单 |
| [docs/decisions.md](docs/decisions.md) | 技术选型 ADR、环境实测、待定项 |
| [docs/prompts.md](docs/prompts.md) | 可直接粘贴给 Codex 的提示词 |
| [docs/research.md](docs/research.md) | 竞品与来源速查 |

## 怎么开工

```
读 AGENTS.md 和 docs/plan.md，按 docs/prompts.md 里的「0. 开场」开始。
```

## 路线图

- [x] Phase 0 桌面层探针（Progman owner / Win+D / Explorer 重启）—— 见 `docs/verify/phase0.md`
- [ ] Phase 1 骨架 + 两栏界面
- [ ] Phase 2 交互
- [ ] Phase 3 健壮性（持久化、DPI、多显示器）
- [ ] Phase 4 托盘 / 设置 / 快捷键
- [ ] Phase 5 打包
