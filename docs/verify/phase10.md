# Phase 10 验收记录 — 卡住清单（想做 ⇄ 卡在什么地方）

日期：2026-11-10
结论：**通过**（两条依赖真实鼠标的交互需人肉眼确认，见文末）

## 1. 交付物

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.Core/BlockersDocument.cs` | `BlockersDocument`（`Rows`、`Find`/`Set`/`Remove`、`ToMarkdown`/`Parse`）、`BlockerRow`、`BlockersStore`（读写 `<数据根>\board\blockers.md`） |
| `src/Wantodo.App/ViewModels/BlockersViewModel.cs` | `BlockerRowViewModel`（两格文本）、`BlockersViewModel`（加行/删行/保存/`ReasonFor`） |
| `src/Wantodo.App/BlockersWindow.xaml(.cs)` | 独立窗口：左「想做」右「卡在什么地方」，加行、删行、打开文件夹、600ms 去抖保存、拖入条目、`Esc` 关闭 |
| `src/Wantodo.App/ViewModels/NoteViewModel.cs` | `BlockerHint` / `HasBlocker` |
| `src/Wantodo.App/MainWindow.xaml(.cs)` | 顶栏「⚠ 卡住」按钮、`ShowBlockers()`、`RefreshBlockerHints()`、`AddNoteToBlockers()`；条目行 `ToolTip` 绑定 `BlockerHint`（`HasBlocker=False` 时置空） |
| `tests/Wantodo.Core.Tests/BlockersDocumentTests.cs` | 9 例：空文本、往返、忽略表头前的说明、`\|` 转义、空原因删行、改原因不重复、大小写/空白归一、UTF-8 无 BOM、缺文件 |

## 2. 命令

```
dotnet build Wantodo.slnx -warnaserror   → 0 警告 0 错误
dotnet test                             → 159 通过 0 失败
```

## 3. 落盘格式

`<数据根>\board\blockers.md`：

```markdown
# 想做 ⇄ 卡在什么地方

| 想做 | 卡在什么地方 |
| --- | --- |
| 复现 StepFinder 全流程 | 数据集标注不全，要先补标 |
| 下载pycharm专业版+连接ssh | 服务端没开 ssh 服务 |
```

- 表头前的说明文字（比如 `# 标题`、段落）读取时忽略，写回时丢弃——文件以应用为准，但**可以被 Obsidian 打开阅读**。
- 单元格里的 `|` 写成 `\|`，换行折成空格。

## 4. 实测

| # | 做了什么 | 结果 |
| --- | --- | --- |
| 1 | 顶栏点「⚠ 卡住」 | 打开 780×560 的独立窗口，标题栏显示文件路径 `E:\Wantodo\board\blockers.md`，日志 `打开卡住清单：…` |
| 2 | 点「加一行」写「复现 StepFinder 全流程」→ Tab → 写原因 | `blockers.md` 出现完整表行，两列都对 |
| 3 | 再加一行「下载pycharm专业版+连接ssh」 | 文件追加第二行；这一行的「想做」与主界面里一条真实条目同名 |
| 4 | 关闭窗口（Esc） | 窗口关闭，主界面 `RefreshBlockerHints()` 重新读文件 |

## 5. 踩坑

- **按钮没有 UIA 名字**：内容全是 `StackPanel` + `TextBlock` 时，`AutomationElement.Name` 是空串，按名字找不到。给顶栏与窗口按钮补了 `AutomationProperties.Name`（顺便也是无障碍信息）。
- **悬停提示无法用合成鼠标验证**：`SetCursorPos` 不产生 WPF 认的 hover 序列，`ToolTip` 不弹。同理 `DoDragDrop` 也收不到合成输入，这两条只能人肉验。
- **桌面层窗口的 UIA 必须用 `FromHandle(hwnd)`**：`RootElement.FindFirst(Children, pid)` 对 owner=Progman 的窗口返回 null；拿 hwnd 还得靠 `EnumWindows` 按 pid 过滤（`FindWindow` 对 WPF 无标题窗口不可靠）。
- 顶栏 4 个按钮时窗口最小宽度变大，4 栏布局下按钮仍不换行（`StackPanel` 横向 + 固定边距）。

## 6. 已知限制

- 匹配规则是「文字归一化后相等」（trim + 忽略大小写）。条目改名后，卡片不会自动跟着改 `blockers.md` 里的那一行，需要手动同步。
- 一个条目只能对应一个原因（同名行会合并）。
- 表格只认两列；多于两列的 Markdown 表格会被丢弃多余列。
- 「打开文件夹」调用 `explorer.exe /select,<文件>`，文件不存在时只打开目录。

## 7. 复跑

```
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
Get-Process Wantodo -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build Wantodo.slnx -warnaserror; dotnet test
Start-Process 'src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.exe'
# 顶栏「⚠ 卡住」→ 加一行 → 填两格 → 关窗 → 在卡片上悬停同名条目
```

## 8. 需要人肉眼确认

Phase 10 当时列了三条；第 1 条（窗口观感，和「随心记」风格是否一致）收口时已目测通过。
下面两条是 Phase 13（D1）要闭环的，合成鼠标做不了，必须真手操。

### 8.1 主界面悬停显示卡住原因

1. 打开 `blockers.md`，确认某条「想做」与主界面里一条条目的文字一致（归一化后相等：忽略首尾空格与大小写）。
   例：表里「想做」是「下载pycharm专业版+连接ssh」。
2. 回到主界面，鼠标移到这条条目上，停住约 0.35 秒。
3. 期望：弹出提示，标题是这条条目的文字，内容是 `blockers.md` 里对应那一行的「卡在什么地方」；
   移开鼠标提示立即消失；条目贴近窗口边缘时提示不被裁掉。
4. 失败长什么样：停 1 秒也不弹；弹出来是空的或显示路径；移开不消失；提示被窗口边缘切掉一半。
5. **实测（2026-11-14）**：用户确认通过 —— 0.35 秒后弹出原因，移开消失，边缘未裁切。

### 8.2 从主界面把条目拖进卡住窗口

1. 顶栏点「⚠ 卡住」打开窗口，把它拖到主界面旁边，两边都看得见。
2. 在主界面按住一条**尚未出现在** `blockers.md` 里的条目拖动；先拖到顶栏「⚠ 卡住」按钮上停一下，再拖进窗口表格区域，松手。
3. 期望：表格新增一行，「想做」列只出现一次该条目的文字，「卡在什么地方」为空；
   主界面里这条条目还在（是复制不是搬走）；窗口仍可继续编辑。
4. 失败长什么样：松手后没有新行；出现两行重复；条目从主界面消失；窗口卡死或抛异常。
   （重复的情况再拖第二次应当不新增，因为按归一化文字去重。）

**实测（2026-11-14）**：两条均由用户确认通过。8.2 当场按用户要求改了落点：不再要求对准表格，拖到窗口任意位置即可（`BlockersWindow.xaml:15-18` 整窗 `AllowDrop` + `PreviewDragOver/PreviewDrop`，`.xaml.cs:178-214`；拖入时显示整窗高亮与「松手：把这条加成新的一行」）。改后 `build -warnaserror` 0 警告 0 错误（15.49s），`test --no-build` 167 通过 / 0 失败（706ms）。

**记录方式**：实测后在 `docs/plan.md:468`、`:469` 勾选并注日期；任何一条失败，把步骤与现象写在这里，标失败，不进下一阶段。
4. 两列表格在 3–4 栏布局下是否可读。
