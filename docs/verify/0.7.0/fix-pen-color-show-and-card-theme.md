# 0.7.1 第三批实机反馈（m01027 / m01031）

用户原文（要点）：

1. 把当前画笔是什么颜色显示出来。
2. 「结构化」画板中，换背景颜色之后，卡片不会立即适配，要等一次操作（比如删掉一张卡）才刷新。
3. 「结构化」改叫「卡片式」，「自由」改叫「画布」。
4. 默认为深色主题。
5. 「待办」卡里选择栏目、新建栏目都点了没反应。

## 1. 工具栏显示当前画笔颜色

- `FreeformBoard` 新增 `public event EventHandler? StrokeStyleChanged;`，在 `SetColorKey`、`SetStrokeWidth`、
  `PinPaletteColorIfNeeded` 真正改了笔色/粗细时触发。
- 同一文件：`SetColorKey` / `SetStrokeWidth` 对「值没变」直接返回，不再重复通知
  （否则工具栏那个 ToggleButton 的开关状态会被无意义地重置）。
- `TopPanelWindow.xaml` 的「色」按钮内容改成 `PenColorSwatch`（12×12 圆点）+ `PenColorLabel`（`#RRGGBB`）。
- `TopPanelWindow.xaml.cs` 新增 `RefreshPenColorSwatch()`：取 `FreeformBoard.StrokeColor`，
  仍是哨兵 `Transparent` 时回退到 `Accent` 资源色；订阅 `StrokeStyleChanged` 与
  `ThemeService.Applied`，构造时先刷一次。

## 2. 换背景/换色板后卡片立即适配

根因：卡片与图元的画刷是在**构建时**用 `FindResource` 抓下来的快照（`CanvasBoard` 建卡片、
`FreeformBoard` 建图元），换主题只替换 `Application.Resources`，`DynamicResource` 的元素会跟着变，
程序化创建的元素不会 —— 所以必须重建一次。

- `ThemeService` 新增 `public static event Action? Applied;`，在 `Apply(...)` 末尾触发。
- `CanvasBoard` 订阅它：`OnThemeApplied() → Rebuild()`（未加载时跳过）。
- `FreeformBoard` 同样订阅：`Rebuild() + UpdateInkLayer() + ApplyCursor()`。

## 3. 表面改名

`TopPanelWindow.xaml`：`SurfaceStructuredButton Content="卡片式"`、`SurfaceFreeformButton Content="画布"`。
内部枚举与日志里的 `structured` / `freeform` 不变（改的是文案，不是数据）。

## 4. 默认深色主题

代码默认本来就是深色：`src/Wantodo.Core/Board.cs:18` `PaletteId = PaletteIds.Dark`，
`App.xaml` 的兜底画刷是 Tokyo Night 深色。用户机器上显示浅色是因为**旧版本迁移**：
`E:\Wantodo\board\data.json` 里 `paletteId` 是 `latte`（迁移前 `theme` 字段留下的偏好）。

因此这批**没有**改代码默认值，也没有替用户改写既有数据（改外观偏好属于用户的选择，静默覆盖会让人莫名其妙）。
切深色：面板状态行的调色板圆点里选深色（`mocha` / `tokyoNight` 等），或告诉我直接改数据文件。

## 5. 待办卡的「选择栏目 / 新建栏目」点了没反应

第一轮改完（`PopupCapture` + `InControl` + 模态守卫）后用户复测：**下拉能打开了，但选中现有栏目仍然没反应**。
`%TEMP%\wantodo-test\data\logs\app-20261119.log` 抓到了确切现场：

```
00:07:01.874 [INFO] [卡片] 待办卡栏目下拉打开 选项=5
00:07:02.317 [INFO] [指针] 全局按下 screen=(971,604) 面板内=True 捕获=ComboBox 弹出层=False 窗口=… pid=8248
00:07:02.470 [INFO] [卡片] 待办卡栏目下拉关闭        ← 没有「待办卡 选择=…」
```

真因：那一瞬的捕获者是 **ComboBox 本身**（不是 `PopupRoot`），而捕获自愈的条件是「有捕获且不是 Popup」，
于是它在按下的瞬间 `Mouse.Capture(null)` —— WPF 的下拉框正是靠这份捕获判定「点了外面」，
清掉等于替用户点了一下外面：下拉立刻关闭，选项收不到点击，也就永远没有 `SelectionChanged`。

修法（收敛自愈的适用范围）：

- `TopEdgePanelService` 新增 `internal static bool IsBoardGesture(object captured)`：
  只有 `FreeformBoard` / `CanvasBoard` / `Tag is CanvasObject` 的卡片才算「画板自己的手势捕获」，
  才可能是泄漏来的；ComboBox / Popup / 输入框持捕获都是正常交互，一律不动。
- 「点面板外收起」的例外从「是不是 Popup」放宽成「这一下有没有元素持有捕获」（`captured is not null`），
  否则点下拉项（弹出层是独立 HWND、可能落在面板矩形外）会把面板收起。
- `CanvasBoard` 的 `SelectionChanged` 里把 `Refresh(force: true)` 改成
  `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)`：不要在 ComboBox 自己的事件里
  把正在收起下拉的控件从可视树上摘掉。

第一轮的两个改动仍然保留且有用：`PopupCapture`（认 `PopupRoot`）、`InControl`（卡片放行卡内控件）、
模态对话框守卫。

诊断打点（供复测回传）：`[卡片] 待办卡栏目下拉打开 选项=N`、`[卡片] 待办卡 选择=<id> 新建=<bool>`、
`[卡片] 新建栏目 名字=… id=…`。

## 验证

```
dotnet build src\Wantodo.App\Wantodo.App.csproj -c Release -warnaserror
  → 0 警告 0 错误

dotnet test tests\Wantodo.Core.Tests -c Release
  → 已通过！失败 0，通过 437，跳过 0

dotnet spike\FreeformProbe\bin\Debug\net10.0-windows\FreeformProbe.dll
  → PROBE PASS
  新增断言：
    换笔色会通知宿主刷新色块 (fired=1)
    换粗细也会通知宿主 (fired=2)
    重复设同一颜色不再通知 (before=2 after=2)
    捕获为空时不算弹出层 (null)
    真正的 Popup 算弹出层 (Popup)
    类型名判据能吃 PopupRoot 这类内部类型 (PopupRoot)
    普通控件类型名不算弹出层 (FreeformBoard)
    ComboBox 的捕获不算画板手势（自愈不能清它） (ComboBox)
    画板自己的捕获才算画板手势 (FreeformBoard)
```

需要人肉眼确认（我这边无法驱动真实鼠标：`mouse_event`/`SendInput` 在这台机器上被丢弃，
`SetCursorPos` 只移动光标不产生点击）：

1. 工具栏「色」按钮上是否有一个圆点 + `#RRGGBB`，点色块换色后圆点/文字是否跟着变。
2. 点状态行调色板圆点换色板，卡片与画布的底色、边框是否**当场**变（不需要先删一张卡）。
3. 两个表面按钮是否显示「卡片式」「画布」。
4. 待办卡里点栏目下拉：下拉是否停在打开状态、能否选中；选「＋ 新建栏目」是否弹出命名框、
   确认后新栏目是否出现并选中。
5. 深色主题：新装默认是深色；现有数据是 `latte`，需要手动切换（见上）。
