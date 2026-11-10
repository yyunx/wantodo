# wantodo

贴在 Windows 桌面上的便签板 —— 像手机主屏的小组件，默认两栏：「要做的事情」「突然想到的问题」。

> **怎么用、怎么装：看 [docs/usage.md](docs/usage.md)（使用说明）**。下面这份 README 偏开发向。

## 现状

**Phase 5 已通过**（2026-11-10）：自包含单文件发布（148.74 MB，不依赖运行时）、
单实例（第二次双击只把已有窗口叫到前面）、`install.ps1` / `uninstall.ps1`
（装到 `%LOCALAPPDATA%\Programs\wantodo`，开始菜单快捷方式，不需要管理员）。
证据见 [`docs/verify/phase5.md`](docs/verify/phase5.md)
（Phase 4 在 [`phase4.md`](docs/verify/phase4.md)；3 在 [`phase3.md`](docs/verify/phase3.md)；
2 / 2.5 在 [`phase2.md`](docs/verify/phase2.md)、[`phase2.5.md`](docs/verify/phase2.5.md)）。

已能用的操作：

- 栏尾输入框回车 → 加一条并保持焦点；`Shift+Enter` 条目内换行
- 双击条目/栏标题 → 就地编辑；`Esc` 取消，`Enter` 提交
- 点勾选圆点或选中行按 `Space` → 划线；`↑/↓` 在条目间移动焦点
- 条目拖到另一栏或同栏其它位置 → 落点线提示
- 顶栏「＋ 栏」插在最近操作的那栏右边，最多 4 栏；栏标题旁 ✕ 删栏（栏里有内容会先确认）
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

数据在 `%APPDATA%\wantodo\data.json`（可直接手改，改完重启生效；日志在同目录 `app.log`）。

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
