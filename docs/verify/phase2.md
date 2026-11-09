# Phase 2 验收记录 —— 可编辑的待办板

- 日期：2026-11-09
- 结论：**通过**（自动化可验证项全过；纯观感项已列出，等肉眼确认）
- 环境：Windows 11 26200，单屏 2560×1600 @ 150%，.NET SDK 10.0.403

## 1. 这一阶段交付了什么

| 层 | 文件 | 内容 |
|---|---|---|
| Core | `src/Wantodo.Core/BoardEditor.cs` | 加栏/删栏/改名/加条目/改文字/勾选/删除/移动，全部纯函数 + `out` 返回失败原因 |
| Core | `src/Wantodo.Core/Note.cs` | 增加 `CompletedAt`（勾选时间，取消勾选要清空） |
| App | `ViewModels/ObservableObject.cs` | `SetProperty` + `PropertyChanged`，无第三方 MVVM 框架 |
| App | `ViewModels/RelayCommand.cs` | `ICommand` 最小实现，支持 `CanExecuteChanged` |
| App | `ViewModels/NoteViewModel.cs` | 编辑态、`EditText`、勾选命令、拖拽落点标记 |
| App | `ViewModels/ColumnViewModel.cs` | 条目集合、草稿输入、标题编辑态、落点高亮 |
| App | `ViewModels/BoardViewModel.cs` | 栏集合、加栏命令、保存编排；**视图不直接碰模型** |
| App | `MainWindow.xaml` | 顶栏（＋栏 / 关闭）、栏卡片模板、条目模板（勾选框/文字/删除）、拖动落点线 |
| App | `MainWindow.xaml.cs` | 双击改名、双击改条目、Enter 提交、Shift+Enter 换行、Esc 取消、↑↓ 换行焦点、空格勾选、拖拽排序 |
| Tests | `tests/Wantodo.Core.Tests/BoardEditorTests.cs` | 14 个用例覆盖编辑器全部行为 |

命令输出：

```
dotnet build Wantodo.slnx -warnaserror
    0 个警告
    0 个错误

dotnet test Wantodo.slnx
已通过! - 失败: 0，通过: 32，已跳过: 0，总计: 32
```

## 2. 实测（真机合成输入 + data.json 核对）

验证方式是：用 Win32 合成鼠标/键盘事件打到真实窗口，然后读 `%APPDATA%\wantodo\data.json` 核对落库结果。不是"看代码觉得对"。

| # | 操作 | 结果 |
|---|---|---|
| 1 | 栏尾输入框输入中文 + 回车 | 落库，输入框清空并保持焦点 |
| 2 | 连续回车加两条 | 两条都在，顺序正确 |
| 3 | 点勾选圆点 | `completed=true`，圆点变蓝底 ✓、文字删除线 + 变灰 |
| 4 | 双击条目文字 | 就地出现编辑框，全选原有文字 |
| 5 | 编辑后回车 | 文本改写落库 |
| 6 | 编辑中 Esc | 内容不落库 |
| 7 | 双击栏标题 | 就地改名，回车生效 |
| 8 | 空格键（行有焦点） | 勾选该行 |
| 9 | ↑ / ↓ | 焦点在条目间移动 |
| 10 | Enter（行有焦点） | 进入该条目的编辑 |
| 11 | Shift+Enter | 条目内换行，落库为 `"买牛奶\r\n顺路取快递"`，界面两行显示 |
| 12 | 点「＋ 栏」 | 2→3→4 栏；到 4 栏按钮置灰 |
| 13 | 栏标题区拖拽 | 只在标题空白区拖动窗口，点标题文字不拖窗口 |
| 14 | 跨栏拖拽条目 | 条目从第 1 栏移到第 3 栏，落库顺序正确 |
| 15 | 点条目右侧 ✕ | 删除该条 |
| 16 | 点空栏 ✕ | 直接删栏，无确认 |
| 17 | 点有内容的栏 ✕ | 弹确认框「「新栏」里还有 1 条，一起删掉？」；是 → 删栏，否 → 保留 |
| 18 | 退出重启 | 栏数、栏名、条目、勾选状态、窗口位置尺寸全部还原 |
| 19 | Explorer 重启 | 仍能自动重挂桌面层（Phase 1 能力的回归验证） |

## 3. 这一阶段修掉的真 bug

1. **Enter 被 TextBox 吃掉**：`AcceptsReturn=True` 时 `KeyDown` 里再判断 Enter 已经晚了，提交逻辑不执行。三个编辑框全部改用 `PreviewKeyDown`。
2. **勾选圆点只有边框变色**：`ControlTemplate` 触发器只设了 `BorderBrush`，没设 `Background` 和 ✓ 的 `Visibility`。补 `TargetName` 到 `Dot` 与 `Tick`。
3. **折叠状态的编辑框抢焦点**：`Loaded` 在元素折叠时也会触发，启动瞬间隐藏的 TextBox 把焦点抢走。改成 `IsVisibleChanged` + 只在 `e.NewValue is true` 时抢。
4. **栏卡片落点高亮不生效**：`BorderBrush="Transparent"` 是本地值，本地值优先级高于样式触发器。把 `BorderThickness`/`BorderBrush` 挪进 `<Border.Style>`。
5. **行焦点虚线框**：`FocusVisualStyle` 造成难看的虚线框，条目行与列表滚动区都设为 `{x:Null}`。
6. **列表滚动区抢走行焦点**：`ScrollViewer` 默认可聚焦，点击时它把焦点从行上抢走，导致空格键不生效。设 `Focusable="False"`。
7. **Ctrl+Enter 换不了行**：WPF `TextBox` 只认不带 Ctrl 的 Enter，`Ctrl+Enter` 既不上屏也不换行。改成 **Shift+Enter 换行、Enter 提交**。

## 4. 合成输入踩到的坑（写给下一次用自动化验收的人）

- `PrintWindow` 对 WPF 窗口会返回**过期帧**，据此判断"编辑框没出现"是错的。用 `CopyFromScreen` 抓窗口区域。
- pwsh 进程必须先 `SetProcessDpiAwarenessContext(-4)`，否则 `GetWindowRect` 返回的是 DIP，坐标会差 1.5 倍。
- 合成双击必须紧凑：两次 down-up 整体 < 150ms，否则 `e.ClickCount` 只到 1。
- `keybd_event(vk, 0, …)` 的 scan code 传 0 时，空格、方向键这类键会被系统丢掉。必须 `MapVirtualKey(vk, 0)` 取真实 scancode。
- 桌面层窗口在浏览器下面时，合成点击打不到它。要测交互，先把 `data.json` 的 `surface` 临时改成 `"Normal"`。
- 确认框可能被别的窗口盖住，用 `EnumChildWindows` 拿按钮 HWND 再 `SendMessage(BM_CLICK)`，比按坐标点可靠。

## 5. 已知限制（不阻塞 Phase 3）

- 栏宽是等分（`UniformGrid`），栏数变多时每栏变窄，草稿提示文字会被裁掉。
- 新增的栏永远追加在最右边。
- 拖动时的落点线只在"插到某一行上方"时可见；拖到列表末尾是整栏高亮，观感还需要人手确认。
- 圆角仍未生效（`DwmSetWindowAttribute` 对 `WindowStyle=None` 无效，见 D7）。

## 6. 怎么复跑

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
cd E:\Dev\Project\wantodo
dotnet build Wantodo.slnx -warnaserror
dotnet test Wantodo.slnx
start .\src\Wantodo.App\bin\Debug\net10.0-windows\Wantodo.App.exe
```

数据文件：`%APPDATA%\wantodo\data.json`；日志：`%APPDATA%\wantodo\app.log`。

## 7. 需要人肉确认的项

- [ ] 双击条目/标题的手感（编辑框出现得够快吗）
- [ ] Shift+Enter 换行是否符合直觉（`Enter` 提交 + `Shift+Enter` 换行）
- [ ] 拖动排序时的落点线是否看得清
- [ ] 4 栏时的拥挤感是否可以接受
- [ ] 勾选动画是否够顺（目前只有颜色/删除线切换，没有过渡动画）
