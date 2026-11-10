# Phase 8 验收：随心记（笔记库）

日期：2026-11-10　目标：像手机便签一样随手记，笔记是可被外部编辑器打开的真实文件。

## 交付物

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.Core/NotesVault.cs` | `Scan` / `Create` / `Read` / `Write` / `Rename` / `Trash`；命名 `yyyyMMdd-HHmm-<slug>.<ext>`；md 写 YAML front matter（title/created/updated/tags）；txt 第一行即标题 |
| `src/Wantodo.Core/Board.cs` | 新增 `DefaultNoteFormat`（`txt` / `md`） |
| `src/Wantodo.Core/JsonBoardStore.cs` | 归一化：只认 `md`，其余一律 `txt` |
| `src/Wantodo.App/NotesWindow.xaml(.cs)` | 左列表（类型徽标 / 标题 / 时间 / 摘要）+ 右编辑器（标题 + 正文）；新建、打开文件夹、删除、自动保存、外部改动自动刷新 |
| `src/Wantodo.App/Notes/NoteListItem.cs` | 列表行展示模型 |
| `src/Wantodo.App/SettingsWindow` | 「随心记默认类型」：纯文本 `.txt` / Markdown `.md` |
| `src/Wantodo.App/MainWindow`、`Shell/TrayIcon` | 顶栏「记」按钮、托盘菜单「随心记…」 |
| `tests/Wantodo.Core.Tests/NotesVaultTests.cs` | 14 个用例（扫描/新建/回读/重名/改名/删除/外部编辑/时间/命名片段） |

## 命令与结果

```
dotnet build Wantodo.slnx -warnaserror   → 0 警告 0 错误
dotnet test                              → 通过 125，失败 0（Phase 7 结束时 108）
```

## 实测

| # | 场景 | 结果 |
| --- | --- | --- |
| 1 | 顶栏「记」/ 托盘「随心记…」 | 打开窗口，标题栏显示当前笔记根 `E:\Wantodo\notes` |
| 2 | 新建（默认 txt）+ 起标题 | 文件从 `20261110-1314-未命名.txt` 改名为 `20261110-1316-第二条随心记.txt`（时间前缀保留） |
| 3 | 正文多行 | 文件内容 = 标题行 + 正文，换行按用户输入的 `\r\n` 保存 |
| 4 | 停手 0.6 秒 | 自动落盘（`StatusText` 显示已保存） |
| 5 | 切到 Markdown 默认类型 | 写 `defaultNoteFormat=md` 到 `board\data.json`，新建得到 `20261110-1317-Markdown-笔记.md` |
| 6 | md front matter | `title` / `created` / `updated` 正确，正文在 `---` 之后 |
| 7 | 外部编辑器改文件 | 2.5 秒内窗口标题与正文刷新为新内容，选中项保持 |
| 8 | 删除 | 二次确认后文件移到 `trash\20261110-131806-20261110-1317-Markdown-笔记.md`，列表少一篇 |
| 9 | 外部新增/删除 | `FileSystemWatcher` 500ms 去抖重扫（与第 7 条同一路径） |

截图：`docs/verify/phase8-notes.png`（左列表两篇、右编辑器、底部状态行）。

## 踩坑

1. **新建即写文件，标题却是占位**：先建 `未命名.txt`、用户再起标题，文件名会一直叫「未命名」。加 `NotesVault.Rename`：只在标题还是占位时改一次名，保留时间前缀，重名加 `-2`。已经存在的文件不自动改名（避免外部链接失效）。
2. **两种格式的标题存法不同**：txt 用第一行当标题（手机便签的直觉），md 用 front matter。解析时 md 缺 front matter 会回退成纯文本解析，避免外部新建的 `.md` 打不开。
3. **自己保存会触发自己的监听**：`FileSystemWatcher` 触发重扫会重置编辑器。加 `_saving` / `_dirty` 两个守卫：正在打字或正在保存时把重扫往后推，不覆盖用户输入。
4. **`FileSystemWatcher` 事件在后台线程**：必须 `Dispatcher.BeginInvoke` 切回 UI 线程再动集合。
5. **列表行三个元素抢位置**：`DockPanel` 默认全部 Dock 到左边，标题和时间会叠在一起；改用三列 `Grid`（徽标 / 标题 / 时间）。

## 已知限制

- 标题改名只做一次（占位 → 正式），之后再改标题不会改文件名：文件名是「创建时的时间戳 + 当时的标题」。
- 没有富文本/Markdown 预览：正文就是纯文本编辑区，md 只是文件格式与 front matter。
- 「是否随主窗一起显示」没做，随心记是独立窗口（从顶栏「记」或托盘打开）。
- 笔记与便签条目之间的「记一笔 / 转为待办」联动属于 P1，本轮没做。

## 复跑

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
Get-Process Wantodo -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build Wantodo.slnx -warnaserror
dotnet test
Start-Process src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe
# 笔记目录
explorer "E:\Wantodo\notes"
```

## 待人工确认

1. 用你自己的编辑器（Obsidian / VS Code）改一篇，回到窗口看是否同步。
2. 中文标题的文件名是否符合你的习惯（例如 `20261110-1316-第二条随心记.txt`）。
3. 新建时默认类型（txt / md）是不是你想要的。
4. 删除进 `trash\` 而不是直接删，是否够用。
5. 窗口大小/位置是否需要记住（当前每次居中打开）。
