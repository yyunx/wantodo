using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wantodo.App.Controls;
using Wantodo.App.ViewModels;
using Wantodo.App.Windows;
using Wantodo.Core;

namespace Wantodo.App.Diagnostics;

/// <summary>
/// README 用的面板截图（启动参数 <c>--shot &lt;目录&gt;</c>）。
///
/// 为什么要在进程内截图：这个面板靠顶边拖拽展开，展开与切面都发生在低级鼠标钩子里，
/// 合成鼠标（<c>SendInput</c>）在这台机器上投不进 WPF，所以「在屏幕上摆好姿势再抓屏」走不通。
/// 这里直接调窗口自己的 <see cref="TopPanelWindow.Expand"/> 与 <see cref="TopPanelWindow.SetSurface"/>，
/// 再用 <see cref="RenderTargetBitmap"/> 渲一遍自己的可视树——截出来的就是产品真实渲染，
/// 而且不含桌面与别的窗口。
/// </summary>
internal static class PanelShot
{
    /// <summary>解析 <c>--shot &lt;目录&gt;</c>；没有这个参数返回 false。</summary>
    public static bool TryGetDirectory(string[] args, out string? directory)
    {
        directory = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--shot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            directory = args[i + 1];
            return !string.IsNullOrWhiteSpace(directory);
        }

        return false;
    }

    public static void Run(
        TopEdgePanelService service,
        BoardViewModel viewModel,
        string directory,
        Action shutdown)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var panel = service.EnsurePanel();
            panel.WindowStartupLocation = WindowStartupLocation.Manual;

            // 截图用的比例：默认 0.75×0.9 在 2560 宽屏上拍出来下半截全是空画布，压扁一点更像一张海报。
            viewModel.Model.PanelWidthRatio = 0.72;
            viewModel.Model.PanelHeightRatio = 0.66;

            panel.Show();
            panel.Width = 1280;
            panel.Expand();
            Pump(700);

            var original = CanvasSurfaces.Normalize(viewModel.Model.Canvas?.ActiveSurfaceId);

            panel.SetSurface(CanvasSurfaces.Structured);
            SeedStructuredDemo(viewModel, panel.CanvasBoard);
            Pump(600);
            Capture(panel, Path.Combine(directory, "panel-cards.png"));

            panel.SetSurface(CanvasSurfaces.Freeform);
            Pump(300);
            SeedCanvasDemo(viewModel, panel.FreeformBoard);
            Pump(400);
            Capture(panel, Path.Combine(directory, "panel-canvas.png"));

            panel.SetSurface(original);
            AppLog.Info($"[截图] 面板截图已输出到 {directory}");
        }
        catch (Exception ex)
        {
            AppLog.Error("[截图] 生成面板截图失败", ex);
        }
        finally
        {
            shutdown();
        }
    }

    /// <summary>
    /// 给「画布」截图临时画几笔：空画板拍出来是一块空底，看不出这是个能画的板子。
    /// 走的是真实输入入口（<see cref="FreeformBoard.PointerDown"/> 等），跟用户手画生成的东西完全一样。
    /// </summary>
    /// <summary>
    /// 把卡片摆成 3 列网格再拍：卡片坐标是画布绝对坐标，直接按用户原来的位置拍，
    /// 窄面板会把右边那张切掉半截。
    /// </summary>
    private static void SeedStructuredDemo(BoardViewModel viewModel, CanvasBoard board)
    {
        var document = viewModel.Model.Canvas;
        if (document is null)
        {
            return;
        }

        for (var i = 0; i < document.Cards.Count; i++)
        {
            var card = document.Cards[i];
            card.W = 370;
            card.H = 228;
            card.X = 30 + (i % 3) * 400;
            card.Y = 24 + (i / 3) * 262;
        }

        board.Rebuild();
    }

    private static void SeedCanvasDemo(BoardViewModel viewModel, FreeformBoard board)
    {
        // 截图用的数据根是副本，先清掉旧涂鸦，免得演示笔画和历史上画的东西叠在一起。
        var document = viewModel.Model.Canvas;
        if (document is not null)
        {
            document.Objects.Clear();
            board.Rebuild();
        }

        board.SetColorKey("#CBA6F7");
        board.SetStrokeWidth(3);

        Stroke(board, [(40, 120), (120, 96), (210, 132), (300, 104), (390, 130), (470, 108)]);
        Stroke(board, [(64, 210), (150, 232), (240, 208), (330, 236)]);

        board.Tool = FreeformTool.Rect;
        board.PointerDown(new Point(70, 290), ModifierKeys.None);
        board.PointerMove(new Point(330, 430));
        board.PointerUp();

        board.Tool = FreeformTool.Arrow;
        board.PointerDown(new Point(360, 356), ModifierKeys.None);
        board.PointerMove(new Point(540, 356));
        board.PointerUp();

        board.Tool = FreeformTool.Ink;
    }

    private static void Stroke(FreeformBoard board, (double X, double Y)[] points)
    {
        board.Tool = FreeformTool.Ink;
        board.PointerDown(new Point(points[0].X, points[0].Y), ModifierKeys.None);
        for (var i = 1; i < points.Length; i++)
        {
            board.PointerMove(new Point(points[i].X, points[i].Y));
        }

        board.PointerUp();
    }

    private static void Capture(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0)
        {
            AppLog.Warn($"[截图] {Path.GetFileName(path)} 尺寸为 0，跳过");
            return;
        }

        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        // 直接 Render(window) 只会得到空白：Window 的内容画在自己的 HwndSource 里，
        // 这里先铺窗口底色（Window.Background 不在内容树里），再把内容元素整块渲上去。
        var background = window.Background ?? Brushes.Transparent;
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        }

        bitmap.Render(backdrop);
        if (window.Content is Visual content)
        {
            bitmap.Render(content);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        AppLog.Info($"[截图] {Path.GetFileName(path)} {width}x{height}");
    }

    /// <summary>把消息队列走空 ms 毫秒：展开与切面都带 160ms 的高度动画，不等就截到半截。</summary>
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
