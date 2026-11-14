# Phase 18 验收 — 新建先命名 + 显示真实文件名（N1）

日期：2026-11-14
结论：**自动项通过**（构建 0 警告、测试 167 通过）；两条肉眼项见第 4 节，**待用户确认**。

## 1. 交付物

| 文件 | 改动 |
|---|---|
| `src/Wantodo.App/Dialogs/NewNoteDialog.xaml` | 新增：无边框弹窗，输入名字，下方实时预览 `yyyyMMdd-HHmm-slug.ext` |
| `src/Wantodo.App/Dialogs/NewNoteDialog.xaml.cs` | 新增：回车确认、Esc 取消、`Loaded` 聚焦；预览用 `NotesVault.Slug` |
| `src/Wantodo.App/Notes/NoteListItem.cs` | 新增 `FileName`（真实文件名）与 `RelativePath`（相对 `notes\` 的路径） |
| `src/Wantodo.App/NotesWindow.xaml` | 列表项加 `ToolTip="{Binding RelativePath}"`；新建按钮提示改为「先起名字」 |
| `src/Wantodo.App/NotesWindow.xaml.cs` | `OnNewClick` 先弹窗再 `_vault.Create`；留空回落「未命名」；状态栏改显示 `FileName` |
| `tests/Wantodo.Core.Tests/NotesVaultTests.cs` | 新增 `Create_WithTitle_PutsSlugIntoFileName`（时间前缀 + slug + 标题回读） |
| `docs/plan.md` / `docs/requirements/frd-0.6.0.md` | 新增 Phase 18 / N1 条目 |

命名规则没动：仍然是 `yyyyMMdd-HHmm-slug.ext`，重名追加 `-2`（见 `src/Wantodo.Core/NotesVault.cs` 的 `Create`/`Slug`）。
「每次改标题都改文件名」仍不做——文件名是外部引用的锚点，理由见 `docs/decisions.md:357-358`。

## 2. 命令与输出

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet build -warnaserror
& $dotnet test --no-build
```

首次构建失败（不是代码问题）：运行中的自身实例 PID 12240 锁住
`src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.Core.dll`，报了 12 个 `MSB3021/MSB3026/MSB3027`。
杀掉该进程后重跑：

```
已成功生成。
    0 个警告
    0 个错误
已用时间 00:00:03.27

已通过! - 失败: 0，通过: 167，已跳过: 0，总计: 167，持续时间: 570 ms
```

## 3. 机制

- 弹窗返回值是「名字字符串」；`OnNewClick` 把它交给 `NotesVault.Create(format, title)`，
  文件从创建那一刻就叫 `yyyyMMdd-HHmm-<slug>.md`，不再先叫「未命名」再改名。
- 留空 → `title = null` → 仍然是「未命名」+ 首次保存时 `Rename` 的老路，两套行为并存。
- 列表悬停显示相对路径（如 `inbox\20261114-1402-买牛奶.md`）；状态栏显示真实文件名，
  和资源管理器里能逐个字符对上。

## 4. 需人肉眼确认（未验证）

### 4.1 有名字新建 → 文件名与标题一致

1. 随心记 → 「＋ 新建」→ 输入 `买牛奶 2026`，看下方预览，回车。
2. 期望：预览是 `文件名：20261114-xxxx-买牛奶-2026.md`；创建后磁盘上真的是这个文件名。
3. 打开「打开文件夹」看到同一文件名；列表里悬停该条，提示里是 `inbox\…-买牛奶-2026.md`；
   状态栏显示 `…-买牛奶-2026.md`。
4. 失败长什么样：文件仍叫 `…-未命名.md`；或悬停/状态栏显示的仍是相对 Id 而不是文件名。

### 4.2 留空新建 → 老路仍可用

1. 再点「＋ 新建」，直接回车（不输入）。
2. 期望：创建「未命名」，光标落在标题框并全选；改标题后保存，文件名同步成新标题的 slug。
3. 失败长什么样：留空直接取消/报错；或改标题后文件名没变（老路被破坏）。

## 5. 已知限制

- 列表里只有悬停才看得到文件名，行内不占第三行；想一眼看全，可看状态栏（选中时）。
- 弹窗内预览的时间前缀取的是弹窗打开时刻，创建发生在几秒内，跨分钟时预览可能与最终文件名差 1 分钟。
- 本轮没有重跑真机肉眼项（第 4 节），plan 里 Phase 18 的两条保持未勾选。
