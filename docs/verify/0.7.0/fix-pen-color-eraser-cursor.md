# 0.7.1 第二批实机反馈：笔色被背景带走 / 橡皮容差 / 工具光标

日期：2026-11-18　用户反馈原文（用户 23:33）：

> 1. 你把画笔的换色给连接到背景的换色上了，我现在切换背景颜色，结果画笔的颜色改了，然后画笔本身的改颜色模块没有反应
> 2. 把橡皮擦的判定范围改大一点
> 3. 然后点击橡皮擦后，鼠标变成橡皮擦的样式，其他也是如此

## 一、根因

### 1）画笔颜色跟着背景走

`src/Wantodo.App/Controls/FreeformBoard.cs` 里默认笔色是「当前强调色」的**实时**读取：

```csharp
public Color StrokeColor { get; set; } = Colors.Transparent;   // accent 的哨兵值
private Color ResolveStrokeColor() =>
    StrokeColor != Colors.Transparent
        ? StrokeColor
        : (FindResource("Accent") as SolidColorBrush)?.Color ?? Colors.White;
```

`ResolveStrokeColor()` 每次落笔都现读一次 `Accent`，而切背景/切主题会重写这个资源，
于是**已经在用的笔色**也一起被改掉，`payload.color` 里存的还是 `"accent"`，
所以重启后依旧跟着主题跑。用户看到的「画笔换色接到了背景换色上」就是这条。

### 2）颜色面板点了没反应

是我在上一批【bug ① 排查】里加的自愈打出来的副作用：
`src/Wantodo.App/Windows/TopEdgePanelService.cs` 在每一次全局左键按下时无条件
`Mouse.Capture(null)`（清掉上一轮手势泄漏的捕获）。而 `ColorPopup` 用的是
`StaysOpen="False"`，Popup 正是靠**鼠标捕获**判定「用户点了外面」——
捕获被钩子清掉的那一刻，Popup 就以为自己被点了外面，在按下的瞬间自己关掉，
色块永远收不到 `MouseLeftButtonUp`。

### 3）橡皮判定范围 = 一根线那么宽

`EraseAt` 走 `HitTest(canvas)`，命中判定是「点是否落在图元的模型矩形里」，没有任何容差：

```csharp
if (canvasPoint.X >= item.X && canvasPoint.X <= item.X + item.W && …)
```

细线（宽 2–3 DIP）与空心矩形几乎点不中，只能精确压到边框上。

### 4）工具没有专属光标

`ApplyCursor()` 只有三种光标：手形（平移/空格）、I 形（文本）、箭头（选择），
其余全部 `Cursors.Cross`，笔和橡皮都是十字。

## 二、改法

| 文件 | 改动 |
| --- | --- |
| `src/Wantodo.App/Controls/FreeformBoard.cs` | 新增 `_followPalette` 与 `PinPaletteColorIfNeeded()`：默认笔色在第一次用到时**快照**成具体色值（`ColorKey` 变 `#RRGGBB`），之后换主题/换背景不再改它；只有用户主动选「跟随色板强调色」才继续跟。调用点：`UpdateInkLayer()`、`BeginStroke()`、`BeginCreate()` |
| 同上 | `HitTest(Point canvasPoint, double tolerance = 0)` 支持外扩；`EraseAt` 用 `const double EraserTolerance = 12`（屏幕 DIP）÷ 当前缩放 = 画布单位 |
| 新增 `src/Wantodo.App/Controls/ToolCursors.cs` | 纯托管画 32×32 字形 → 内存里拼 `.cur` 字节 → `new Cursor(stream)`；失败回退 `Cursors.Cross`。笔、橡皮、放大镜各一个，热点分别落在笔尖/橡皮中心/镜心 |
| 同上 | `ApplyCursor()` 映射：Ink→笔尖、Eraser→橡皮、Zoom→放大镜、Text→IBeam、Select→Arrow、Pan/空格/平移→Hand、图形→Cross |
| `src/Wantodo.App/Windows/TopPanelWindow.xaml.cs` | 新增 `IsAnyPopupOpen => ColorPopup.IsOpen \|\| ColumnPicker.IsDropDownOpen`；色块/粗细按钮选完把 `ToolColor.IsChecked` 复位（否则下一次点「色」只会关掉它，看起来还是「没反应」） |
| `src/Wantodo.App/Windows/TopEdgePanelService.cs` | 捕获自愈加前置条件 `!_panel.IsAnyPopupOpen`；「点面板外收起」的例外从 `IsPickerOpen` 换成 `IsAnyPopupOpen` |

## 三、回归锁（`spike/FreeformProbe`，新增 `ToolingChecks()`）

```
PASS 默认笔色落成具体色值（不再保存 accent）            (key=#696969)
PASS 换强调色不改已经拿在手里的笔色                     (key=#696969 color=#FF696969)
PASS 显式选了「跟随色板」才继续跟                       (key=accent color=#00FFFFFF)
PASS 橡皮擦掉边框外 8 DIP 的目标                        (objects=0)
PASS 橡皮不会擦到 50 DIP 外的目标                       (objects=1)
PASS 笔工具有专属笔尖光标
PASS 橡皮工具有专属橡皮光标
PASS 缩放工具有专属放大镜光标
PASS 切到橡皮后控件光标跟着换
PASS 切到画笔后控件光标跟着换
PASS 文本工具仍是 I 形光标                              (IBeam)
```

## 四、构建与测试

```
dotnet build src\Wantodo.App\Wantodo.App.csproj -c Release -warnaserror  → 0 警告 0 错误
dotnet test  tests\Wantodo.Core.Tests -c Release                          → 通过 437，失败 0
dotnet spike\FreeformProbe\bin\Debug\net10.0-windows\FreeformProbe.dll    → PROBE PASS
```

## 五、需要人肉眼确认

1. 切背景/换主题后，画笔颜色**不再**跟着变；再点「色」面板选红/蓝，新笔迹立刻是那个颜色。
2. 橡皮在离线条一两毫米的地方按下去也能擦掉。
3. 切换笔/橡皮/缩放时，鼠标指针形状跟着换（笔尖、橡皮、放大镜）。
