# wantodo

贴在 Windows 桌面上的便签板 —— 像手机主屏的小组件，默认两栏：「要做的事情」「突然想到的问题」。

## 现状

**Phase 1 已通过**（2026-11-09）：能看、能存、能读的两栏小组件已经跑在桌面层上。
证据见 [`docs/verify/phase1.md`](docs/verify/phase1.md)（含窗口截图 `docs/verify/phase1-window.png`）。
当前版本**只能看，不能编辑**——增删改、勾选、拖拽排序是 Phase 2，提示词见
[`docs/prompts.md`](docs/prompts.md) 的「2.6 Phase 2 实现」。

已验证的窗口语义：`SetWindowLongPtr(GWLP_HWNDPARENT, Progman)` 设 owner 即可贴在桌面图标层，
Win+D 不受影响，Explorer 重启后靠 `TaskbarCreated` 重挂，全程不需要分层窗口
（`exstyle` 无 `WS_EX_LAYERED`，硬件加速完好）。这一代 Windows 没有可用的 WorkerW 壁纸层，
所以宿主按 **Progman 优先、WorkerW 兜底**探测。

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet build Wantodo.slnx -warnaserror
dotnet test tests\Wantodo.Core.Tests\Wantodo.Core.Tests.csproj
Start-Process src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.App.exe
```

数据在 `%APPDATA%\wantodo\data.json`（可直接手改，改完重启生效；日志在同目录 `app.log`）。

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
- [x] Phase 1 骨架 + 两栏界面（只读、能存能读、数据驱动）—— 见 `docs/verify/phase1.md`
- [ ] Phase 2 交互
- [ ] Phase 3 健壮性（持久化、DPI、多显示器）
- [ ] Phase 4 托盘 / 设置 / 快捷键
- [ ] Phase 5 打包
