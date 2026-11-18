# 色板圆点显示背景色 + 面板截图流水线（0.6.0）

日期：2026-11-19
对应反馈：m01547「背景颜色这里选择之后和切换之后的背景颜色不匹配」；m01705「把圆点里的颜色改成背景的颜色就好，不要去改背景」。

## 1. 圆点画错色

`src/Wantodo.App/Windows/TopPanelWindow.xaml.cs` 的 `BuildPaletteDots()` 原来拿 `colors.Accent` 画色点。
用户按色点挑的是「底会长什么样」，结果选完底色跟色点完全不是一个色——色点看着像强调色，换成深色底之后就对不上了。

改法：色点直接画该色板的 `colors.CardBackground`（窗口根 XAML 的 `Background="{DynamicResource CardBackground}"`，也就是面板底色），
选中项用 `TextPrimary` 描 2 DIP，未选中用 `BorderSubtle` 描 1 DIP（近白的底色在浅色面板上仍看得见）。
ToolTip 写「名称（背景 #RRGGBB）」。**没有改主题本身**——只改预览色。

## 2. 截图怎么来的（合成鼠标不可用）

这台机器上合成鼠标投不进 WPF：`SetCursorPos` 能移动光标、`SendInput` 返回 sent=2，
但 `E:\Wantodo\logs\*.log` 里连一条 `[指针] 全局按下` 都没有；真人点击就有。所以「在屏幕上摆好姿势再抓屏」走不通。

改成进程内渲染：新增 `src/Wantodo.App/Diagnostics/PanelShot.cs`，启动参数 `--shot <目录>` 时
拿真实面板窗口 → `Expand()` → `SetSurface("structured")` 拍一张 → `SetSurface("freeform")` 再拍一张 → 退出。

- `TopEdgePanelService.EnsurePanel()`（新增，internal）：不靠鼠标在顶边停 120ms 才建窗口
- `TopPanelWindow.SetSurface` 由 `private` 放宽到 `internal`
- `App.OnStartup` 末尾：`PanelShot.TryGetDirectory(e.Args, out var dir)` 命中则 `Dispatcher.BeginInvoke(...)`

踩到的坑（都写进代码注释了）：

1. `RenderTargetBitmap.Render(window)` 直接渲 Window 得到的是**纯白空图**（两张 PNG 都是 10376 B）。
   要先把 `window.Background` 画进一个 `DrawingVisual` 垫底（`Window.Background` 不在内容树里），
   再 `bitmap.Render((Visual)window.Content)`。
2. 默认 `PanelWidthRatio=0.75 / PanelHeightRatio=0.9` 在 2560 宽的屏上拍出来下半截全是空画布，
   截图模式改成 0.72 / 0.66，并 `panel.Width = 1280`。
3. 卡片坐标是画布绝对坐标，直接拍会把右边那张切掉半截 → 截图前把 4 张卡摆成 3 列网格
   （`SeedStructuredDemo`）；画布那一面先清空旧图元再 `PointerDown/Move/Up` 画演示笔画
   （`SeedCanvasDemo`，走的是真实输入入口，不是伪造视觉）。
4. 展开与切面都带 160ms 高度动画，不等就截到半截 → `Pump(ms)` 用 `DispatcherFrame` 等。

截图数据根是**副本**（`%TEMP%\wantodo-shot\`，`portable.txt`=「shot」），不碰 `E:\Wantodo`。

## 3. 产物

`docs/images/panel-cards.png`（1920×1008）、`docs/images/panel-canvas.png`（1920×1008）。

## 4. 验证

```text
> dotnet build src\Wantodo.App\Wantodo.App.csproj -c Release -warnaserror
    0 个警告
    0 个错误

> dotnet test tests\Wantodo.Core.Tests -c Release
已通过! - 失败: 0，通过: 437，已跳过: 0，总计: 437

> dotnet spike\FreeformProbe\bin\Debug\net10.0-windows\FreeformProbe.dll
PROBE PASS

> Wantodo.exe --shot <目录>（便携副本，mocha 色板）
[截图] panel-cards.png 1920x1008
[截图] panel-canvas.png 1920x1008
```

需要人肉眼确认的：色点在 7 套色板下是否都和面板底色一致（浅色色板下也要看得出边界）；
换色后卡片、图形、色点是否即时跟着换。
