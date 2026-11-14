# Phase 11 验收记录 — 随心记光标不再跳到末尾（B1）

日期：2026-11-14
结论：**通过**。自动项：构建 0 警告、测试 166 通过；两条肉眼项由用户在 19:19 于带修复的 Debug 构建上实测通过（见第 5 节）。

## 1. 交付物

| 位置 | 内容 |
| --- | --- |
| `src/Wantodo.Core/NoteWriteTracker.cs` | `NoteWriteStamp`（路径/长度/mtime/记录时刻）、`NoteWriteTracker`：`Capture`、`IsNotePath`、`IsSelfWrite`；`MatchWindow=5s`、`TimeTolerance=1000ms` |
| `tests/Wantodo.Core.Tests/NoteWriteTrackerTests.cs` | 7 例：笔记路径判定、真文件自写匹配、长度不符、mtime 容差内外、路径不符、签名过期、文件已删除 |
| `src/Wantodo.App/NotesWindow.xaml.cs:31` | `_selfWrites` 签名表（UI 线程独占） |
| `src/Wantodo.App/NotesWindow.xaml.cs:131` | `OnFileSystemChanged`：先 `IsNotePath` 过滤（`*.tmp`、隐藏文件直接丢），取 `FileInfo` 长度/mtime，回到 UI 线程比对签名，命中即丢弃，否则才重启 500ms 去抖计时器 |
| `src/Wantodo.App/NotesWindow.xaml.cs:176` | `RememberSelfWrite`：记录签名、清理过期项、上限 16 条 |
| `src/Wantodo.App/NotesWindow.xaml.cs:196` | `Reload` 把 `preserveCaret: keepSelection` 传下去 |
| `src/Wantodo.App/NotesWindow.xaml.cs:231` | `LoadIntoEditor(item, preserveCaret)`：同一篇且文本未变 → 不碰 TextBox；文本变了才赋值并还原 `CaretIndex`/`VerticalOffset`；切换条目仍把光标放末尾 |
| `src/Wantodo.App/NotesWindow.xaml.cs:347` | `FlushSave` 读完 mtime 后调用 `RememberSelfWrite`（改名后的新路径也正确） |

## 2. 命令与输出

```
dotnet build -warnaserror
  Wantodo.Core -> ...\bin\Debug\net10.0\Wantodo.Core.dll
  Wantodo.App  -> ...\bin\Debug\net10.0-windows\Wantodo.dll
  已成功生成。 0 个警告 0 个错误（16.52 秒）

dotnet test
  已通过! - 失败: 0，通过: 166，已跳过: 0，总计: 166，持续时间: 650 ms
```

（Phase 10 结束时是 159 例，本阶段新增 7 例。）

## 3. 改动为什么能修掉跳转

原来的链路：`FlushSave` 写文件 → `FileSystemWatcher` 把**自己这次写盘**（含 `*.tmp` 的创建/改名事件）当外部改动 → 500ms 后 `Reload` → `LoadIntoEditor` 重设 `TitleBox.Text`/`BodyBox.Text`，并把 `CaretIndex` 打到 `Text.Length`。

三条一起断：

1. **认出自写**：写完立刻记 `(路径, 长度, mtime)`；事件回来三项都对得上、且签名在 5 秒内，就当自己写的丢弃。签名单次写盘会留多条（Changed/Renamed/Created 各来一遍），所以命中后不删除、靠 5 秒过期，避免第二条事件漏网。
2. **不认 `*.tmp`**：`WriteFile` 是 `*.tmp` → 覆盖移动，临时文件事件与笔记无关，`IsNotePath` 直接挡掉。
3. **文本没变就不碰编辑器**：同一篇重载时比较 `Title`/`Body` 与 TextBox 当前文本；相等就只换快照对象，TextBox 一个属性都不设，光标与滚动自然原样。文本真被外部改了才赋值，并先记 `CaretIndex`/`VerticalOffset`、赋值后还原（`Math.Clamp` + `UpdateLayout` + `ScrollToVerticalOffset`）。

## 4. 自动能验证的部分

| # | 做了什么 | 结果 |
| --- | --- | --- |
| 1 | `dotnet build -warnaserror` | 0 警告 0 错误 |
| 2 | `dotnet test` | 166 通过 0 失败 |
| 3 | 单测覆盖「自写命中」「长度不符」「mtime 差 1.5 秒视为外部」「路径不符」「签名过期」「删除文件」 | 全部按预期 |

## 5. 人肉眼实测（2026-11-14 用户确认通过）

### 5.1 自己输入后光标不跳（核心验收）— 通过

1. 打开随心记，新建一篇 500 字左右的笔记（直接粘贴几段文字），保存完成（状态栏出现「已保存」）。
2. 用鼠标点进正文中间某处，把光标停在一个字后面；记住这一刻的滚动位置。
3. 连续输入 20 个汉字，停手等 **3 秒**（等自动保存与一次扫描都跑完）。
4. 期望：光标仍在刚输入的最后一个字后面；页面没有滚动；20 个字一个不少。
5. 失败长什么样：光标瞬移到文末、视图跳到最底部，或输入的字被旧内容覆盖。

实测结果：用户复测通过（第一次失败是因为跑在 0.5.0 安装版上，见第 6 节）。

### 5.2 外部编辑器改了仍然刷新 — 通过

1. 保持随心记窗口开着、选中同一篇笔记。
2. 用记事本打开 `<数据根>\notes\...\那篇.md`，在末尾加一行「外部改的」并保存。
3. 期望：**≤2.5 秒**内列表与编辑器出现这行；如果你此刻正在随心记里打字，先不覆盖你输入的内容，等你停手保存后再刷新。
4. 失败长什么样：界面一直不更新；或刚敲的字被磁盘内容盖掉。

实测结果：用户于 Phase 11 期间确认通过。

## 6. 已知限制与踩坑

- 自写签名有 5 秒窗口、mtime 容差 1 秒：若外部程序恰好在这 5 秒内写入**完全相同长度**且 mtime 落在容差内，会被当自己写的而漏一次刷新。概率极低；漏了也会在下一次外部改动时补上。
- 文本比较用 `Ordinal`（逐字符）。txt 正文末尾的换行会被 `RenderPlainText`/解析各裁一次，因此同一篇的重载有时会走「赋值 + 还原光标」那条路，光标位置由还原逻辑兜住，不会回到开头。
- 首轮测试挂过一次：`Capture_ThenIsSelfWrite_MatchesFileJustWritten` 用了固定时刻 `Now = 2026-11-14 12:00 UTC` 当「现在」，而签名记录用的是真实 `UtcNow`，导致签名被判过期；改成 `DateTimeOffset.UtcNow` 后通过——测试里不要拿写死的时刻代表「现在」。
- **验收时踩的坑**：第一次复测跑的是 `C:\Users\ty\AppData\Local\Programs\wantodo\Wantodo.exe`（0.5.0 安装版，11-13 启动的旧进程），修复只在当天的 Debug 构建里，所以现象照旧。
- **Debug 构建需要运行时**：本项目 Debug 是框架依赖（FDD），而机器上只有 SDK 10.0.403 装在用户目录，apphost 不认，直接双击会弹「You must install .NET Desktop Runtime」。本地跑要先设 `$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"`，或 `dotnet run --project src\Wantodo.App`；0.5.0 安装版是自包含发布，所以从来没弹过。
