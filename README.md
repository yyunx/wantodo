# wantodo

贴在 Windows 桌面上的便签板 —— 像手机主屏的小组件，默认两栏：「要做的事情」「突然想到的问题」。

## 现状

设计阶段。代码还没开始写，先做桌面层探针（Phase 0）。

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

- [ ] Phase 0 桌面层探针（WorkerW / Win+D / Explorer 重启）
- [ ] Phase 1 骨架 + 两栏界面
- [ ] Phase 2 交互
- [ ] Phase 3 健壮性（持久化、DPI、多显示器）
- [ ] Phase 4 托盘 / 设置 / 快捷键
- [ ] Phase 5 打包
