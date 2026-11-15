using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wantodo.Core;
using IOPath = System.IO.Path;

namespace Wantodo.App.Controls;

/// <summary>
/// 自由表面宿主（0.7.0 P0.4 渲染 / P1.1 交互）：渲染 <see cref="CanvasObject"/>，
/// 并按 <see cref="CanvasViewport"/> 做缩放与平移。与结构化表面共用同一份
/// <see cref="CanvasDocument"/>、同一个窗口、同一套主题——切换时只改 <c>Visibility</c>，
/// 不重建控件、不换窗口。
/// </summary>
/// <remarks>
/// 0.7.0 P1.1 的交互范围：十工具的选择与互斥、缩放 25–400%（含「适应」）、
/// 手形/空格平移、选择工具的点选与框选、拖动图元、橡皮删除。
/// 创建类工具（文本/图片/矩形/线/箭头/画笔）在 P1.2 接入，本步只保证「选中即高亮、
/// 光标正确、不吞手势」；撤销栈也在 P1.2。
/// </remarks>
public sealed class FreeformBoard : Grid
{
    private readonly Canvas _plane = new();
    private readonly Canvas _overlay = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _offset = new(0, 0);
    private readonly Dictionary<string, FrameworkElement> _visuals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
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

    private FreeformTool _tool = FreeformTool.Select;
    private bool _spaceHeld;

    private bool _panning;
    private Point _panOrigin;
    private double _panStartX;
    private double _panStartY;

    private bool _marquee;
    private Point _marqueeStart;
    private Rectangle? _marqueeVisual;

    private bool _moving;
    private Point _moveOrigin;
    private readonly Dictionary<string, Point> _moveStart = new(StringComparer.Ordinal);
    private bool _dirty;

    public FreeformBoard()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Focusable = true;
        FocusVisualStyle = null;

        var transform = new TransformGroup { Children = { _scale, _offset } };
        _plane.RenderTransform = transform;
        _overlay.IsHitTestVisible = false;
        _plane.IsHitTestVisible = false;

        Children.Add(_plane);
        Children.Add(_overlay);
        Children.Add(_empty);

        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        UseLayoutRounding = true;
    }

    /// <summary>本控件负责的表面 id（默认就是自由表面）。</summary>
    public string SurfaceId { get; set; } = CanvasSurfaces.Freeform;

    public CanvasDocument? Document => _document;

    /// <summary>当前工具；设置即刷新光标。互斥由调用方（工具条）保证，这里只处理「当前是哪个」。</summary>
    public FreeformTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            ApplyCursor();
        }
    }

    /// <summary>空格临时切手形：由窗口的 PreviewKeyDown 驱动，松开即复原。</summary>
    public bool SpaceHeld
    {
        get => _spaceHeld;
        set
        {
            if (_spaceHeld == value)
            {
                return;
            }

            _spaceHeld = value;
            ApplyCursor();
        }
    }

    public double Zoom => _document?.ViewportOf(SurfaceId).Zoom ?? 1.0;

    public IReadOnlyCollection<string> Selected => _selected;

    public int ObjectCount => _document?.Objects.Count(o => Matches(o)) ?? 0;

    /// <summary>缩放/平移结束后触发，供状态行刷新（不参与保存）。</summary>
    public event EventHandler? ViewportChanged;

    public event EventHandler? SelectionChanged;

    public void Attach(CanvasDocument document, Action? persist, string? imagesDir)
    {
        _document = document ?? new CanvasDocument();
        _persist = persist;
        _imagesDir = imagesDir;
        _selected.Clear();
        Rebuild();
    }

    /// <summary>文档变了（切表面回来、撤销、外部导入）就整块重画；控件本身常驻。</summary>
    public void Rebuild()
    {
        _plane.Children.Clear();
        _overlay.Children.Clear();
        _visuals.Clear();
        _marqueeVisual = null;

        if (_document is null)
        {
            UpdateEmpty();
            return;
        }

        var viewport = _document.ViewportOf(SurfaceId);
        ApplyZoomAndOffset(viewport.Zoom, viewport.OffsetX, viewport.OffsetY);

        foreach (var item in _document.Objects.Where(Matches).OrderBy(o => o.ZIndex))
        {
            var element = BuildObject(item);
            if (element is null)
            {
                continue;
            }

            var wrapper = Wrap(item, element);
            Canvas.SetLeft(wrapper, item.X);
            Canvas.SetTop(wrapper, item.Y);
            Panel.SetZIndex(wrapper, item.ZIndex);
            _plane.Children.Add(wrapper);
            _visuals[item.Id] = wrapper;
        }

        // 文档里已经没有的 id 不能继续算「选中」
        _selected.RemoveWhere(id => !_visuals.ContainsKey(id));
        ApplySelectionVisuals();
        UpdateEmpty();
    }

    private bool Matches(CanvasObject item) =>
        string.Equals(CanvasSurfaces.Normalize(item.Surface), SurfaceId, StringComparison.OrdinalIgnoreCase);

    private void UpdateEmpty()
    {
        _empty.Visibility = ObjectCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- 缩放与平移 ---------------------------------------------------------

    private void ApplyZoomAndOffset(double zoom, double offsetX, double offsetY)
    {
        _scale.ScaleX = FreeformZoom.Clamp(zoom);
        _scale.ScaleY = _scale.ScaleX;
        _offset.X = offsetX;
        _offset.Y = offsetY;
    }

    /// <summary>以 <paramref name="anchor"/>（控件坐标）为锚点缩放，锚点下的画布内容不跑。</summary>
    public void ZoomBy(double factor, Point? anchor = null)
    {
        var document = _document;
        if (document is null || factor <= 0)
        {
            return;
        }

        var viewport = document.ViewportOf(SurfaceId);
        var before = FreeformZoom.Clamp(viewport.Zoom);
        var target = FreeformZoom.Clamp(before * factor);
        if (Math.Abs(target - before) < 0.0001)
        {
            return;
        }

        var point = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        // 锚点下的画布坐标在缩放前后保持不变
        var canvasX = (point.X - viewport.OffsetX) / before;
        var canvasY = (point.Y - viewport.OffsetY) / before;
        var offsetX = point.X - (canvasX * target);
        var offsetY = point.Y - (canvasY * target);

        ApplyZoomAndOffset(target, offsetX, offsetY);
        SaveViewport(target, offsetX, offsetY);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把缩放重置为 100%，并把视口左上角对回原点。</summary>
    public void ResetZoom() => SetViewport(1.0, 0, 0);

    /// <summary>适应内容：按内容外框与面板尺寸算缩放，再居中。</summary>
    public void FitToView()
    {
        var document = _document;
        if (document is null)
        {
            return;
        }

        var items = document.Objects.Where(Matches).ToList();
        if (items.Count == 0)
        {
            ResetZoom();
            return;
        }

        var left = items.Min(o => o.X);
        var top = items.Min(o => o.Y);
        var right = items.Max(o => o.X + o.W);
        var bottom = items.Max(o => o.Y + o.H);
        var zoom = FreeformZoom.Fit(right - left, bottom - top, ActualWidth, ActualHeight);
        var offsetX = (ActualWidth - ((right - left) * zoom)) / 2 - (left * zoom);
        var offsetY = (ActualHeight - ((bottom - top) * zoom)) / 2 - (top * zoom);
        SetViewport(zoom, offsetX, offsetY);
    }

    private void SetViewport(double zoom, double offsetX, double offsetY)
    {
        var document = _document;
        if (document is null)
        {
            return;
        }

        ApplyZoomAndOffset(zoom, offsetX, offsetY);
        SaveViewport(FreeformZoom.Clamp(zoom), offsetX, offsetY);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveViewport(double zoom, double offsetX, double offsetY)
    {
        var document = _document;
        if (document is null)
        {
            return;
        }

        var viewport = document.ViewportOf(SurfaceId);
        viewport.Zoom = FreeformZoom.Clamp(zoom);
        viewport.OffsetX = offsetX;
        viewport.OffsetY = offsetY;
        _dirty = true;
        Flush();
    }

    private void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _persist?.Invoke();
    }

    private Point ToCanvas(Point point) =>
        new((point.X - _offset.X) / Math.Max(0.0001, _scale.ScaleX),
            (point.Y - _offset.Y) / Math.Max(0.0001, _scale.ScaleY));

    private Point ToBoard(Point canvasPoint) =>
        new((canvasPoint.X * _scale.ScaleX) + _offset.X, (canvasPoint.Y * _scale.ScaleY) + _offset.Y);

    // ---- 命中与选择 ---------------------------------------------------------

    /// <summary>画布坐标下点中了哪个图元（取 Z 最高的那个）。</summary>
    private CanvasObject? HitTest(Point canvasPoint)
    {
        var document = _document;
        if (document is null)
        {
            return null;
        }

        CanvasObject? hit = null;
        foreach (var item in document.Objects.Where(Matches))
        {
            if (canvasPoint.X >= item.X && canvasPoint.X <= item.X + item.W &&
                canvasPoint.Y >= item.Y && canvasPoint.Y <= item.Y + item.H)
            {
                if (hit is null || item.ZIndex >= hit.ZIndex)
                {
                    hit = item;
                }
            }
        }

        return hit;
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        _selected.Clear();
        ApplySelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectOnly(string id)
    {
        _selected.Clear();
        _selected.Add(id);
        ApplySelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleSelection(string id)
    {
        if (!_selected.Remove(id))
        {
            _selected.Add(id);
        }

        ApplySelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySelectionVisuals()
    {
        foreach (var (id, element) in _visuals)
        {
            if (element is not Border border)
            {
                continue;
            }

            var selected = _selected.Contains(id);
            border.BorderThickness = new Thickness(selected ? 1 : 0);
            if (selected)
            {
                border.SetResourceReference(Border.BorderBrushProperty, "Accent");
            }
        }
    }

    /// <summary>删除选中图元（P1.1 无撤销；P1.2 接撤销栈后走同一入口）。</summary>
    public void DeleteSelected()
    {
        var document = _document;
        if (document is null || _selected.Count == 0)
        {
            return;
        }

        var removed = document.Objects.RemoveAll(o => _selected.Contains(o.Id));
        if (removed == 0)
        {
            return;
        }

        _selected.Clear();
        Rebuild();
        _dirty = true;
        Flush();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteObject(string id)
    {
        var document = _document;
        if (document is null)
        {
            return;
        }

        if (document.Objects.RemoveAll(o => string.Equals(o.Id, id, StringComparison.Ordinal)) == 0)
        {
            return;
        }

        _selected.Remove(id);
        Rebuild();
        _dirty = true;
        Flush();
    }

    // ---- 指针手势（鼠标事件与探针共用同一入口）------------------------------

    /// <summary>
    /// 指针按下。<paramref name="board"/> 是控件坐标。
    /// 鼠标事件与自动化探针都走这个入口——「平移不与框选串」这类行为要能自动验证，
    /// 而不是只在成品里手点一遍。
    /// </summary>
    public void PointerDown(Point board, ModifierKeys modifiers)
    {
        if (_document is null)
        {
            return;
        }

        var canvas = ToCanvas(board);
        var shift = (modifiers & ModifierKeys.Shift) != 0;

        if (_tool == FreeformTool.Zoom)
        {
            ZoomBy(shift ? 1 / FreeformZoom.Step : FreeformZoom.Step, board);
            return;
        }

        if (_tool == FreeformTool.Pan || _spaceHeld)
        {
            _panning = true;
            _panOrigin = board;
            _panStartX = _offset.X;
            _panStartY = _offset.Y;
            ApplyCursor();
            return;
        }

        if (_tool == FreeformTool.Eraser)
        {
            if (HitTest(canvas) is { } target)
            {
                DeleteObject(target.Id);
            }

            return;
        }

        if (_tool != FreeformTool.Select)
        {
            // 创建类工具在 P1.2 接入：这里明确记一条，避免「点了没反应也看不出为什么」。
            AppLog.Info($"[画布] 工具 {FreeformToolCatalog.IdOf(_tool)} 的创建行为在 P1.2 接入");
            return;
        }

        var hit = HitTest(canvas);
        if (hit is not null)
        {
            if (shift)
            {
                ToggleSelection(hit.Id);
            }
            else if (!_selected.Contains(hit.Id))
            {
                SelectOnly(hit.Id);
            }

            _moving = true;
            _moveOrigin = board;
            _moveStart.Clear();
            foreach (var item in _document.Objects.Where(o => _selected.Contains(o.Id)))
            {
                _moveStart[item.Id] = new Point(item.X, item.Y);
            }

            return;
        }

        if (!shift)
        {
            ClearSelection();
        }

        _marquee = true;
        _marqueeStart = board;
        _marqueeVisual = new Rectangle
        {
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _marqueeVisual.SetResourceReference(Shape.StrokeProperty, "Accent");
        _overlay.Children.Add(_marqueeVisual);
    }

    public void PointerMove(Point board)
    {
        if (_panning)
        {
            _offset.X = _panStartX + (board.X - _panOrigin.X);
            _offset.Y = _panStartY + (board.Y - _panOrigin.Y);
            return;
        }

        if (_moving)
        {
            var dx = (board.X - _moveOrigin.X) / Math.Max(0.0001, _scale.ScaleX);
            var dy = (board.Y - _moveOrigin.Y) / Math.Max(0.0001, _scale.ScaleY);
            foreach (var item in _document?.Objects ?? [])
            {
                if (!_moveStart.TryGetValue(item.Id, out var start))
                {
                    continue;
                }

                item.X = start.X + dx;
                item.Y = start.Y + dy;
                if (_visuals.TryGetValue(item.Id, out var element))
                {
                    Canvas.SetLeft(element, item.X);
                    Canvas.SetTop(element, item.Y);
                }

                _dirty = true;
            }

            return;
        }

        if (_marquee && _marqueeVisual is not null)
        {
            var rect = new Rect(_marqueeStart, board);
            Canvas.SetLeft(_marqueeVisual, rect.Left);
            Canvas.SetTop(_marqueeVisual, rect.Top);
            _marqueeVisual.Width = rect.Width;
            _marqueeVisual.Height = rect.Height;

            // 框选按画布坐标算相交，和缩放无关
            var box = new Rect(ToCanvas(rect.TopLeft), ToCanvas(rect.BottomRight));
            _selected.Clear();
            foreach (var item in _document?.Objects.Where(Matches) ?? [])
            {
                if (box.IntersectsWith(new Rect(item.X, item.Y, item.W, item.H)))
                {
                    _selected.Add(item.Id);
                }
            }

            ApplySelectionVisuals();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PointerUp()
    {
        if (_panning)
        {
            _panning = false;
            SaveViewport(_scale.ScaleX, _offset.X, _offset.Y);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
            ApplyCursor();
            return;
        }

        if (_moving)
        {
            _moving = false;
            _moveStart.Clear();
            _dirty = true;
            Flush();
            return;
        }

        if (_marquee)
        {
            _marquee = false;
            if (_marqueeVisual is not null)
            {
                _overlay.Children.Remove(_marqueeVisual);
                _marqueeVisual = null;
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_document is null)
        {
            return;
        }

        Focus();
        PointerDown(e.GetPosition(this), Keyboard.Modifiers);
        if (_panning || _moving || _marquee)
        {
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerMove(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasActive = _panning || _moving || _marquee;
        PointerUp();
        if (wasActive)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_document is null)
        {
            return;
        }

        ZoomBy(e.Delta > 0 ? FreeformZoom.Step : 1 / FreeformZoom.Step, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void ApplyCursor()
    {
        Cursor = _panning || _spaceHeld || _tool == FreeformTool.Pan
            ? Cursors.Hand
            : _tool switch
            {
                FreeformTool.Text => Cursors.IBeam,
                FreeformTool.Select => Cursors.Arrow,
                _ => Cursors.Cross,
            };
    }

    // ---- 渲染 ---------------------------------------------------------------

    private Border Wrap(CanvasObject item, FrameworkElement element)
    {
        var border = new Border
        {
            Width = item.W,
            Height = item.H,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            SnapsToDevicePixels = true,
            Child = element,
        };

        if (_selected.Contains(item.Id))
        {
            border.BorderThickness = new Thickness(1);
            border.SetResourceReference(Border.BorderBrushProperty, "Accent");
        }

        return border;
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
            return MissingItem(item);
        }

        var full = IOPath.IsPathRooted(path) ? path : IOPath.Combine(_imagesDir, path);
        if (!File.Exists(full))
        {
            // 图片被用户删了：不崩、不抛，画一个虚线框提示。
            return MissingItem(item);
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

    private static FrameworkElement MissingItem(CanvasObject item)
    {
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
