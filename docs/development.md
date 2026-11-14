# 项目开发文档

> 5 分钟看懂 wantodo：它是什么、由哪些部分组成、数据放在哪、改之前该读哪份文件。
> 本文只做索引与总览：行为细节看 `docs/spec.md`，阶段与验收看 `docs/plan.md`，
> 技术取舍看 `docs/decisions.md`，硬性规则原文在 `AGENTS.md`。

## 1. 是什么

Windows 桌面常驻便签。窗口挂在桌面图标层（WorkerW），像手机主屏的小组件；默认两栏
（「要做的事情」「突然想到的问题」），栏数可增减。随手记、单手键盘操作，不弹窗打扰。

- 使用者：独立开发者的自用工具，离线优先，本地文件是唯一事实来源。
- 口味：简洁优先、轻量优先；宁可少一个功能，不加一层界面。
- 平台：Windows 10 1809+ / Windows 11；`net10.0-windows` / WPF / C# 13 / `Nullable=enable`。
  .NET 10 LTS 支持到 2028-11-14。

## 2. 架构总览

单进程 WPF，三层：

| 层 | 项目 | 职责 | 约束 |
|---|---|---|---|
| 纯逻辑 | `src/Wantodo.Core` | 模型、持久化、布局树、笔记库、主题取色 | 不引用 WPF，必须可单测 |
| 宿主 | `src/Wantodo.App` | 窗口、ViewModel、托盘、设置 | `DllImport` 只允许出现在 `Interop/` |
| 测试 | `tests/Wantodo.Core.Tests` | xUnit，当前 159 例 | 只测 Core |

第三方 NuGet：只有测试项目的 xUnit，产品零第三方依赖（这一条是产品约束，不是现状）。

### 2.1 窗口拓扑

| 窗口 | 作用 | 关键文件 |
|---|---|---|
| MainWindow | 桌面层主便签：顶栏 + 停靠栏 + 条目 | `src/Wantodo.App/MainWindow.xaml` |
| NotesWindow | 随心记：左侧列表 / 右侧编辑器 | `src/Wantodo.App/NotesWindow.xaml` |
| BlockersWindow | 卡住清单：两列 Markdown 表格 | `src/Wantodo.App/BlockersWindow.xaml` |
| QuickCaptureWindow | 全局快捷键 `Ctrl+Alt+Space` 快速记一条 | `src/Wantodo.App/QuickCaptureWindow.xaml` |
| SettingsWindow | 外观 / 尺寸 / 数据根 / 自启 | `src/Wantodo.App/SettingsWindow.xaml` |
| ColumnNameDialog | 栏改名 | `src/Wantodo.App/Dialogs/ColumnNameDialog.xaml` |

托盘（`src/Wantodo.App/Shell/TrayIcon.cs`）是常驻入口；单实例用命名 Mutex
（`src/Wantodo.App/App.xaml.cs:36`），第二次启动只把已有窗口唤到前面。

### 2.2 桌面层（最不确定的一块）

`src/Wantodo.App/Interop/DesktopHost.cs` 找 WorkerW 并挂窗口；`Interop/DesktopLayer.cs` 处理
Win+D 之后与 Explorer 重启之后的重新挂载；`Interop/ShellRestartWatcher.cs` 监听 `TaskbarCreated`。
改动前先读 `docs/verify/phase0.md` 与 `docs/verify/phase3.md`，并先写探针——这块的结论全部来自实测，
不存在「按文档应该能跑」。

## 3. 核心模块

| 模块 | 职责 | 关键文件 | 可单测 |
|---|---|---|---|
| 模型 | 条目、栏、看板（schema v2） | `src/Wantodo.Core/Note.cs`、`NoteColumn.cs`、`Board.cs` | 是 |
| 编辑操作 | 加 / 删 / 移 / 勾选条目与栏 | `src/Wantodo.Core/BoardEditor.cs` | 是 |
| 持久化 | JSON 读写、原子替换、bak / corrupt 回退 | `src/Wantodo.Core/JsonBoardStore.cs` | 是 |
| 布局树 | 停靠二叉树、比例、尺寸推算 | `src/Wantodo.Core/LayoutNode.cs`、`LayoutMetrics.cs` | 是 |
| 位置守卫 | 多显示器与分辨率变化时纠正窗口位置 | `src/Wantodo.Core/PlacementGuard.cs`、`WindowPlacement.cs` | 是 |
| 笔记库 | 文件即数据：命名、扫描、读写、回收站 | `src/Wantodo.Core/NotesVault.cs` | 是 |
| 卡住清单 | Markdown 两列表格解析与写回 | `src/Wantodo.Core/BlockersDocument.cs` | 是 |
| 数据根 | 目录布局、指针解析、迁移、保留策略、锁 | `src/Wantodo.Core/DataPaths.cs`、`DataRootResolver.cs`、`DataMigrator.cs`、`DataRetention.cs`、`RootLock.cs` | 是 |
| 主题 | 主题令牌与配色计算 | `src/Wantodo.Core/ThemePalette.cs` | 是 |
| 主题应用 | 把令牌写进 `Application.Resources` | `src/Wantodo.App/Appearance/ThemeService.cs` | 否（依赖 WPF） |
| 视图模型 | 绑定、命令、集合通知 | `src/Wantodo.App/ViewModels/BoardViewModel.cs`、`ColumnViewModel.cs`、`NoteViewModel.cs` | 否 |

## 4. 数据与持久化

用户数据放在可迁移的数据根（默认不在系统盘）：

```text
<数据根>\
  board\data.json          (+ data.bak / data.corrupt-<ts>.json)
  board\blockers.md
  notes\inbox | notes\yyyy\MM
  logs\app-yyyyMMdd.log    (保留 14 天)
  trash\                   (保留 30 天)
  config\settings.json     (留位，设置目前仍在 board\data.json)
  .lock
```

- 指针顺序：`HKCU\Software\Wantodo\DataRoot` → `location.json` → 程序目录（非 C:）→ 固定盘按剩余
  空间 → 兜底程序目录并告警。实现见 `src/Wantodo.App/Storage/WindowsDataRootEnvironment.cs`。
- 写入：`*.tmp` → `Flush(true)` → 已存在用 `File.Replace`（留 bak），首次用 `File.Move`；
  遇到 `IOException` 降级为 Copy + Move 并记 WARN。启动容忍半截文件并回退 `.bak`，
  再失败隔离为 `.corrupt-<ts>.json` 后起空白库。
- 退出前强制落盘一次，见 `src/Wantodo.App/App.xaml.cs:107-116`（`OnSessionEnding`）。

## 5. 性能基线（0.5.0 实测，改动前后用同一方法）

| 指标 | 值 | 出处 |
|---|---|---|
| 首帧 | 5 次 1069 / 805 / 811 / 801 / 835 ms，中位 811 ms | `docs/verify/phase3.md:35-39` |
| private（稳定 60 s） | 73–86 MB | `docs/verify/phase3.md:43` |
| SC 包 zip | 63,869,489 B | `publish/wantodo-0.5.0-win-x64.zip` |
| FDD 包 zip | 196,936 B | `publish/wantodo-0.5.0-fdd-win-x64.zip` |

Working Set 会被系统裁剪，**private 才是基线**。测量条件与命令写在 `docs/verify/phase3.md`、
`docs/verify/phase5.md`；0.6.0 的对照基线见 `docs/verify/baseline-0.6.0.md`。

## 6. 开发规范索引

不复述条文，只给入口：

- 硬性规则（P/Invoke 位置、构建必须绿、禁 `async void`、原子落盘、禁 `AllowsTransparency`、
  不改系统设置、Windows 行为先探针）→ `AGENTS.md`
- 阶段完成定义、一次一个阶段的门控 → `AGENTS.md` 末节，执行计划见 `docs/plan.md:1-16`
- 已有取舍与否决记录（D1–D17）→ `docs/decisions.md`
- 每阶段的命令与实测输出 → `docs/verify/phaseN.md`
- 使用说明（用户视角）→ `docs/usage.md`

## 7. 术语表

| 术语 | 含义 |
|---|---|
| 板 / 看板（Board） | 一份 `data.json`：栏、条目、布局树、主题、位置 |
| 栏 / pane | 一列便签；schema v2 里是布局树的叶子 |
| 停靠树 | 二叉布局树，节点是分栏或叶子，见 `src/Wantodo.Core/LayoutNode.cs` |
| 数据根 | 存放 board / notes / logs / trash 的目录，可在设置里迁移 |
| 随心记（Notes） | `notes\` 下的普通文件，可被 Obsidian / 记事本打开 |
| 卡住清单 | `board\blockers.md`，两列 Markdown 表格 |
| 主题令牌 | `App.xaml` 里的 `DynamicResource` 画刷；换主题直接改值，不重建窗口 |
| 桌面层 | WorkerW 上的窗口，Win+D 后仍可见 |
