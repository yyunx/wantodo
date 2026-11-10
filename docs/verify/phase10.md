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

1. 顶栏「⚠ 卡住」→ 窗口是否好认（和「随心记」风格是否一致）。
2. 把主界面里的条目拖到那张表上，是否稳定加一行（不重复、不丢）。
3. 悬停带原因的条目，提示是否在 0.35 秒后出现、移开即消失、没有被窗口边缘裁掉。
4. 两列表格在 3–4 栏布局下是否可读。
