using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wantodo.App.Controls;

/// <summary>
/// 画板各工具的光标。WPF 自带的 <see cref="Cursors"/> 里没有笔和橡皮，
/// 这里在内存里画一张 32×32 位图再拼成 <c>.cur</c> 交给 <see cref="Cursor(Stream)"/>：
/// 不下载资源、不落外部文件。任何一步失败都回退到系统十字光标，
/// 光标只是提示，不该让画板起不来。
/// </summary>
internal static class ToolCursors
{
    private const int Size = 32;
    private const int IconDirSize = 6;
    private const int IconEntrySize = 16;
    private const int BitmapInfoHeaderSize = 40;

    private static readonly Lazy<Cursor?> PenCursor = new(() => Build(DrawPen, 5, 26));

    private static readonly Lazy<Cursor?> EraserCursor = new(() => Build(DrawEraser, 16, 16));

    private static readonly Lazy<Cursor?> ZoomCursor = new(() => Build(DrawZoom, 13, 13));

    public static Cursor Pen => PenCursor.Value ?? Cursors.Cross;

    public static Cursor Eraser => EraserCursor.Value ?? Cursors.Cross;

    public static Cursor Zoom => ZoomCursor.Value ?? Cursors.Cross;

    private static Cursor? Build(Action<DrawingContext> draw, int hotX, int hotY)
    {
        try
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                draw(context);
            }

            var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var stride = Size * 4;
            var pixels = new byte[stride * Size];
            bitmap.CopyPixels(pixels, stride, 0);
            Unpremultiply(pixels);

            var maskStride = (Size + 31) / 32 * 4;
            var maskBytes = maskStride * Size;
            var imageBytes = BitmapInfoHeaderSize + (stride * Size) + maskBytes;
            using var stream = new MemoryStream(IconDirSize + IconEntrySize + imageBytes);
            using var writer = new BinaryWriter(stream);

            // ICONDIR：保留字 0、类型 2（光标）、图像数 1
            writer.Write((ushort)0);
            writer.Write((ushort)2);
            writer.Write((ushort)1);

            // ICONDIRENTRY：宽高（0 表示 256）、色数、保留、热点、图像字节数、偏移
            writer.Write((byte)Size);
            writer.Write((byte)Size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)hotX);
            writer.Write((ushort)hotY);
            writer.Write(imageBytes);
            writer.Write(IconDirSize + IconEntrySize);

            // BITMAPINFOHEADER：高度写两倍（XOR 位图 + AND 掩码）
            writer.Write(BitmapInfoHeaderSize);
            writer.Write(Size);
            writer.Write(Size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0); // BI_RGB
            writer.Write(stride * Size);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            // XOR 位图：DIB 是自下而上
            for (var y = Size - 1; y >= 0; y--)
            {
                writer.Write(pixels, y * stride, stride);
            }

            // AND 掩码全 0：让 Windows 按颜色位图的 alpha 抠图
            writer.Write(new byte[maskBytes]);
            writer.Flush();

            stream.Position = 0;
            return new Cursor(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>WPF 只给预乘 alpha，写进 DIB 前要还原成直通 alpha。</summary>
    private static void Unpremultiply(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha is 0 or 255)
            {
                continue;
            }

            pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / alpha);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
        }
    }

    private static void DrawPen(DrawingContext context)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x2B, 0x2E, 0x36));
        var halo = new Pen(Brushes.White, 6.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        var shaft = new Pen(ink, 3.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        context.DrawLine(halo, new Point(25, 6), new Point(11, 20));
        context.DrawLine(shaft, new Point(25, 6), new Point(11, 20));

        var nib = new StreamGeometry();
        using (var geometry = nib.Open())
        {
            geometry.BeginFigure(new Point(6, 26), true, true);
            geometry.LineTo(new Point(12, 21.5), true, false);
            geometry.LineTo(new Point(8.5, 18), true, false);
        }

        context.DrawGeometry(ink, new Pen(Brushes.White, 2), nib);
    }

    private static void DrawEraser(DrawingContext context)
    {
        var body = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x7A));
        var edge = new Pen(Brushes.White, 2.6) { LineJoin = PenLineJoin.Round };

        context.PushTransform(new RotateTransform(-35, 16, 16));
        var area = new Rect(9, 12.5, 15, 9.5);
        context.DrawRoundedRectangle(body, edge, area, 2.6, 2.6);
        context.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)),
            null,
            new Rect(9.6, 12.9, 13.8, 3.4),
            1.4,
            1.4);
        context.Pop();
    }

    private static void DrawZoom(DrawingContext context)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x2B, 0x2E, 0x36));
        var halo = new Pen(Brushes.White, 6.5);
        var ring = new Pen(ink, 3);
        var plus = new Pen(ink, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var plusHalo = new Pen(Brushes.White, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        context.DrawEllipse(null, halo, new Point(13, 13), 7.2, 7.2);
        context.DrawLine(halo, new Point(18.4, 18.4), new Point(25.6, 25.6));
        context.DrawEllipse(null, ring, new Point(13, 13), 7.2, 7.2);
        context.DrawLine(ring, new Point(18.4, 18.4), new Point(25.6, 25.6));

        context.DrawLine(plusHalo, new Point(9.6, 13), new Point(16.4, 13));
        context.DrawLine(plusHalo, new Point(13, 9.6), new Point(13, 16.4));
        context.DrawLine(plus, new Point(9.6, 13), new Point(16.4, 13));
        context.DrawLine(plus, new Point(13, 9.6), new Point(13, 16.4));
    }
}
