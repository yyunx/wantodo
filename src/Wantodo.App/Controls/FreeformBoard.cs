using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wantodo.Core;

namespace Wantodo.App.Controls;

/// <summary>
/// 自由表面宿主（0.7.0 P0.4）：只渲染 <see cref="CanvasObject"/>，并按
/// <see cref="CanvasViewport"/> 做缩放/平移。与结构化表面共用同一份
/// <see cref="CanvasDocument"/>、同一个窗口、同一套主题——切换时只改 <c>Visibility</c>，
/// 不重建控件、不换窗口。
/// </summary>
/// <remarks>
/// 0.7.0 P0.4 的渲染范围：图片（按显示尺寸解码）与文本两种；笔迹/图形由 P1.2 补。
/// 三个视图（结构化卡片、自由表面、将来的更多表面）都从同一份文档取数据，
/// 所以「不丢内容」靠的是文档，不是控件缓存。
/// </remarks>
public sealed class FreeformBoard : Grid
{
    private readonly Canvas _plane = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _offset = new(0, 0);
    private readonly TextBlock _empty = new()
    {
        Text = "还没有内容",
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };

    private CanvasDocument? _document;
    private Action? _persist;
    private string? _imagesDir;

    public FreeformBoard()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;

        _plane.RenderTransform = new TransformGroup { Children = { _scale, _offset } };
        Children.Add(_plane);
        Children.Add(_empty);

        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
    }

    /// <summary>本控件负责的表面 id（默认就是自由表面）。</summary>
    public string SurfaceId { get; set; } = CanvasSurfaces.Freeform;

    public CanvasDocument? Document => _document;

    public void Attach(CanvasDocument document, Action? persist, string? imagesDir)
    {
        _document = document ?? new CanvasDocument();
        _persist = persist;
        _imagesDir = imagesDir;
        Rebuild();
    }

    /// <summary>文档变了（切表面回来、撤销、外部导入）就整块重画；控件本身常驻。</summary>
    public void Rebuild()
    {
        _plane.Children.Clear();

        if (_document is null)
        {
            UpdateEmpty();
            return;
        }

        var viewport = _document.ViewportOf(SurfaceId);
        _scale.ScaleX = viewport.Zoom;
        _scale.ScaleY = viewport.Zoom;
        _offset.X = viewport.OffsetX;
        _offset.Y = viewport.OffsetY;

        foreach (var item in _document.Objects
                     .Where(o => string.Equals(CanvasSurfaces.Normalize(o.Surface), SurfaceId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(o => o.ZIndex))
        {
            var element = BuildObject(item);
            if (element is null)
            {
                continue;
            }

            Canvas.SetLeft(element, item.X);
            Canvas.SetTop(element, item.Y);
            Panel.SetZIndex(element, item.ZIndex);
            _plane.Children.Add(element);
        }

        UpdateEmpty();
    }

    private void UpdateEmpty()
    {
        var count = _document?.Objects.Count(o =>
            string.Equals(CanvasSurfaces.Normalize(o.Surface), SurfaceId, StringComparison.OrdinalIgnoreCase)) ?? 0;
        _empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement? BuildObject(CanvasObject item)
    {
        switch (item.Kind)
        {
            case CanvasObject.KindImage:
                return BuildImage(item);
            case CanvasObject.KindText:
                return BuildText(item);
            default:
                // 笔迹/图形留给 P1.2；先画一个方框占位，避免「数据在但看不见」。
                var placeholder = new Border
                {
                    Width = item.W,
                    Height = item.H,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                };
                placeholder.SetResourceReference(Border.BorderBrushProperty, "BorderSubtle");
                return placeholder;
        }
    }

    private FrameworkElement? BuildImage(CanvasObject item)
    {
        var path = item.Get("path");
        if (string.IsNullOrWhiteSpace(path) || _imagesDir is null)
        {
            return null;
        }

        var full = Path.IsPathRooted(path) ? path : Path.Combine(_imagesDir, path);
        if (!File.Exists(full))
        {
            // 图片被用户删了：不崩、不抛，画一个虚线框提示。
            var missing = new Border
            {
                Width = item.W,
                Height = item.H,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
            };
            missing.SetResourceReference(Border.BorderBrushProperty, "BorderSubtle");
            return missing;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(full, UriKind.Absolute);
        // 按显示尺寸解码，不把原图整张塞进显存（V-07 的内存前提）
        var pixelWidth = (int)Math.Round(item.W * _scale.ScaleX);
        if (pixelWidth > 0)
        {
            bitmap.DecodePixelWidth = pixelWidth;
        }

        bitmap.EndInit();
        bitmap.Freeze();

        return new Image
        {
            Source = bitmap,
            Width = item.W,
            Height = item.H,
            Stretch = Stretch.Uniform,
        };
    }

    private FrameworkElement BuildText(CanvasObject item)
    {
        var text = new TextBlock
        {
            Text = item.Get("text") ?? string.Empty,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Width = item.W,
            MaxHeight = item.H,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        return text;
    }
}
