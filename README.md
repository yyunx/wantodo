# wantodo

贴在 Windows 桌面上的便签板 —— 像手机主屏的小组件，默认两栏：「要做的事情」「突然想到的问题」。

> **怎么用、怎么装：看 [docs/usage.md](docs/usage.md)（使用说明）**。下面这份 README 偏开发向。

## 现状

**Phase 7 已通过**（2026-11-10）：数据根外置——所有数据不再固定放在 C 盘，按「`portable.txt` → 注册表
指针 → 程序目录 → 空间最大的非系统盘」选址；设置里可把数据整体迁移到别的盘并自动重启；v0.4 旧数据
首次启动自动导入。证据见 [`docs/verify/phase7.md`](docs/verify/phase7.md)
（Phase 6 在 [`phase6.md`](docs/verify/phase6.md)；5 在 [`phase5.md`](docs/verify/phase5.md)；4 在
[`phase4.md`](docs/verify/phase4.md)；3 在 [`phase3.md`](docs/verify/phase3.md)；2 / 2.5 在
[`phase2.md`](docs/verify/phase2.md)、[`phase2.5.md`](docs/verify/phase2.5.md)）。

自包含单文件发布（148.74 MB，不依赖运行时）、单实例（第二次双击只把已有窗口叫到前面）、
`install.ps1` / `uninstall.ps1`（装到 `%LOCALAPPDATA%\Programs\wantodo`，开始菜单快捷方式，不需要管理员）。

已能用的操作：

- 栏尾输入框回车 → 加一条并保持焦点；`Shift+Enter` 条目内换行
- 双击条目/栏标题 → 就地编辑；`Esc` 取消，`Enter` 提交
- 点勾选圆点或选中行按 `Space` → 划线；`↑/↓` 在条目间移动焦点
- 条目拖到另一栏或同栏其它位置 → 落点线提示
- 顶栏「＋ 栏」插在最近操作的那栏右边，最多 4 栏；栏标题旁 ✕ 删栏（栏里有内容会先确认）
- 加栏自动撑宽到 280 DIP/栏、删栏自动收窄；手动拖过窗口就转「固定」，设置里可切回「跟随栏数」
- 每栏最窄 190 DIP，窗口放不下就横向滚动，不会把文字挤成一条缝
- 托盘左键：显示/隐藏（召回不夺焦点）；托盘右键：设置、层级、外观、隐藏已完成、开机自启、退出
- `Ctrl+Alt+Space`：贴屏幕上方弹出「快速记一条」，回车入库、Esc 取消
- 设置面板：桌面层/普通窗口/置顶、深色/浅色、色调强度、隐藏已完成、开机自启（写 `HKCU\...\Run`，可回滚）

已验证的窗口语义：`SetWindowLongPtr(GWLP_HWNDPARENT, Progman)` 设 owner 即可贴在桌面图标层，
Win+D 不受影响，Explorer 重启后靠 `TaskbarCreated` 重挂，全程不需要分层窗口
（`exstyle` 无 `WS_EX_LAYERED`，硬件加速完好）。这一代 Windows 没有可用的 WorkerW 壁纸层，
所以宿主按 **Progman 优先、WorkerW 兜底**探测。

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet build Wantodo.slnx -warnaserror
dotnet test tests\Wantodo.Core.Tests\Wantodo.Core.Tests.csproj
Start-Process src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe
```

数据根（业务数据都在这，**默认不在 C 盘**）：开发版是 `src\Wantodo.App\bin\Debug\net10.0-windows\data`，
安装版按「空间最大的非系统盘」选，本机为 `E:\Wantodo`。当前实际路径看设置面板「数据位置」或
`HKCU\Software\wantodo` 的 `DataRoot`。板数据在 `<根>\board\data.json`（可直接手改，改完重启生效），
日志在 `<根>\logs\app-yyyyMMdd.log`，笔记在 `<根>\notes\`，回收站在 `<根>\trash\`。

## 安装 / 卸载

```powershell
# 先出发布产物（自包含单文件，约 149 MB）
dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\sc

# 装到 %LOCALAPPDATA%\Programs\wantodo + 开始菜单快捷方式（不加 -DesktopShortcut 就不建桌面图标）
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1

# 卸载：默认保留数据；要连便签一起删就加 -RemoveData
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

- 分发用 **self-contained 单文件**：framework-dependent 版只有 1.37 MB，但要求机器上有 .NET 10
  Desktop Runtime，双击会弹 `You must install .NET to run this application`。
- 未经签名，首次运行 SmartScreen 会拦一下（「更多信息 → 仍要运行」）。
- 安装只影响当前用户，不写 `Program Files`、不需要管理员；控制面板「应用」里看不到，卸载用 `uninstall.ps1`。

## 文档

| 文件 | 作用 |
|---|---|
| [AGENTS.md](AGENTS.md) | 给 Codex 的仓库约定（技术栈、硬性规则、完成定义） |
| [docs/usage.md](docs/usage.md) | **使用说明**：下载安装、日常操作、快捷键、数据备份、常见问题 |
| [docs/spec.md](docs/spec.md) | 产品规格：场景、功能分级、数据模型、交互细节 |
| [docs/plan.md](docs/plan.md) | 分阶段计划 + 每阶段验收清单 |
| [docs/decisions.md](docs/decisions.md) | 技术选型 ADR、环境实测、待定项 |
| [docs/prompts.md](docs/prompts.md) | 可直接粘贴给 Codex 的提示词 |
| [docs/research.md](docs/research.md) | 竞品与来源速查 |
| [docs/design/data-root.md](docs/design/data-root.md) | 数据根目录设计（不落 C 盘、指针、迁移状态机） |
| [docs/design/dock-layout.md](docs/design/dock-layout.md) | 停靠式自由布局设计（布局树、热区、比例、迁移） |

## 怎么开工

```
读 AGENTS.md 和 docs/plan.md，按 docs/prompts.md 里的「0. 开场」开始。
```

## 路线图

- [x] Phase 0 桌面层探针（Progman owner / Win+D / Explorer 重启）—— 见 `docs/verify/phase0.md`
- [x] Phase 1 骨架 + 两栏界面（只读、能存能读、数据驱动）—— 见 `docs/verify/phase1.md`
- [x] Phase 2 交互（增删改、勾选划线、拖拽排序、栏操作）—— 见 `docs/verify/phase2.md`
- [x] Phase 2.5 布局优化（加栏插在当前栏右边、最小栏宽 190 DIP、窄了横向滚动）—— 见 `docs/verify/phase2.5.md`
- [x] Phase 3 健壮性（写盘节流、位置兜底、系统事件、启动开销）—— 见 `docs/verify/phase3.md`
- [x] Phase 4 托盘 / 设置 / 快捷键 / 主题 —— 见 `docs/verify/phase4.md`
- [x] Phase 5 打包（单实例 + 安装脚本）—— 见 `docs/verify/phase5.md`
- [x] Phase 6 窗口尺寸自适应闭环（删栏收窄 / AutoFit 与 Manual 双态）—— 见 `docs/verify/phase6.md`
- [x] Phase 7 数据根外置（不落 C 盘、可迁移）—— 见 `docs/verify/phase7.md`
- [ ] Phase 8 随心记（笔记库 + 默认类型可配 + 外部可编辑）
- [ ] Phase 9 停靠式自由布局（栏停靠到任意位置、单独缩放）—— 设计见 `docs/design/dock-layout.md`
- [ ] Phase 10 0.5.0 收口（回归矩阵 + 发布说明）
