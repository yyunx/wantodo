using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wantodo.Core;
using IOPath = System.IO.Path;

namespace Wantodo.App.Controls;

/// <summary>
/// 自由表面宿主（0.7.0 P0.4 渲染 / P1.1 交互 / P1.2 创建与撤销）：渲染 <see cref="CanvasObject"/>，
/// 并按 <see cref="CanvasViewport"/> 做缩放与平移。与结构化表面共用同一份
/// <see cref="CanvasDocument"/>、同一个窗口、同一套主题——切换时只改 <c>Visibility</c>，
/// 不重建控件、不换窗口。
/// </summary>
/// <remarks>
/// P1.2 起：矩形/线/箭头/文本可创建，笔画由 <see cref="InkCanvas"/> 承载并落 ISF，
/// 撤销按表面分开（<see cref="SurfaceUndo"/>，深度 50，只存单表面快照）。
/// </remarks>
public sealed class FreeformBoard : Grid
{
    private readonly Canvas _plane = new();
    private readonly Canvas _overlay = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _offset = new(0, 0);
    private readonly Dictionary<string, FrameworkElement> _visuals = new(StringComparer.Ordinal);

    /// <summary>图形图元的可视元素（绝对坐标，不套 <see cref="Wrap"/>，所以单独记）。</summary>
    private readonly Dictionary<string, Shape> _shapeVisuals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>笔迹层：只承载 stroke 图元，其余图元由 <see cref="_plane"/> 自绘（plan §4.8）。</summary>
    private readonly InkCanvas _ink = new();

    /// <summary>每个笔迹图元对应的 ISF 内容；重建时按 <c>payload.path</c> 回读。</summary>
    private readonly Dictionary<string, StrokeCollection> _strokes = new(StringComparer.Ordinal);

    /// <summary>每个表面一条撤销栈（P1.2，plan §4.6）。</summary>
    private readonly SurfaceUndo _undo = new();

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
    private string? _inkDir;

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

    /// <summary>框选/拖动是否已经压过撤销（一次手势只压一份）。</summary>
    private bool _gestureUndo;

    /// <summary>手势开始时抓的快照，等真的发生位移才压栈（纯点击不污染撤销栈）。</summary>
    private string? _pendingUndo;

    /// <summary>最后一次指针位置（控件坐标）；PointerUp 不带参数，落点要用它。</summary>
    private Point _lastBoard;

    private bool _creating;
    private Point _createStart;
    private FrameworkElement? _createPreview;

    private TextBox? _textEditor;
    private CanvasObject? _editingItem;

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

        // 笔迹层跟内容层吃同一套缩放/平移；非笔迹工具时整层不参与命中，鼠标照样落到本控件。
        _ink.RenderTransform = new TransformGroup { Children = { _scale, _offset } };
        _ink.Background = Brushes.Transparent;
        _ink.EditingMode = InkCanvasEditingMode.None;
        _ink.IsHitTestVisible = false;
        _ink.UseCustomCursor = true;
        _ink.Cursor = Cursors.Pen;
        _ink.StrokeCollected += OnStrokeCollected;

        Children.Add(_plane);
        Children.Add(_ink);
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
            if (_tool == value)
            {
                return;
            }

            CommitTextEdit(commit: true);
            _tool = value;
            UpdateInkLayer();
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

    /// <summary>InkCanvas 里当前的笔画数，探针用来确认「模型有笔迹」和「画布上真看得见」是一回事。</summary>
    internal int InkStrokeCount => _ink.Strokes.Count;

    /// <summary>某个图元当前的视觉元素（探针用来确认图片真的没外框）。</summary>
    internal FrameworkElement? VisualOf(string id) => _visuals.TryGetValue(id, out var element) ? element : null;

    /// <summary>缩放/平移结束后触发，供状态行刷新（不参与保存）。</summary>
    public event EventHandler? ViewportChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>图片工具点下去时触发：文件对话框由宿主开，落位点记在 <see cref="PendingImagePoint"/>。</summary>
    public event EventHandler? ImagePickRequested;

    /// <summary>图片工具点击处的画布坐标；宿主选完文件后交给 <see cref="AddImageObject"/>。</summary>
    public Point? PendingImagePoint { get; private set; }

    public void Attach(CanvasDocument document, Action? persist, string? imagesDir, string? inkDir = null)
    {
        _document = document ?? new CanvasDocument();
        _persist = persist;
        _imagesDir = imagesDir;
        _inkDir = inkDir;
        _selected.Clear();
        _undo.Clear(SurfaceId);
        Rebuild();
    }

    /// <summary>当前表面还能撤销几步。</summary>
    public int UndoDepth => _undo.Depth(SurfaceId);

    public bool CanUndo => _undo.CanUndo(SurfaceId);

    public bool CanRedo => _undo.CanRedo(SurfaceId);

    /// <summary>撤销本表面一步（不动另一表面，plan §4.6）。</summary>
    public void Undo()
    {
        if (_document is null || !_undo.TryUndo(SurfaceId, Snapshot(), out var json))
        {
            return;
        }

        CanvasSnapshot.Restore(_document, SurfaceId, json);
        AfterRestore();
    }

    public void Redo()
    {
        if (_document is null || !_undo.TryRedo(SurfaceId, Snapshot(), out var json))
        {
            return;
        }

        CanvasSnapshot.Restore(_document, SurfaceId, json);
        AfterRestore();
    }

    private string Snapshot() => _document is null ? string.Empty : CanvasSnapshot.Capture(_document, SurfaceId);

    private void PushUndo()
    {
        if (_document is not null)
        {
            _undo.Push(SurfaceId, Snapshot());
        }
    }

    /// <summary>撤销/重做之后：丢掉已不存在的选择、清掉过期的笔迹缓存、重画并落盘。</summary>
    private void AfterRestore()
    {
        if (_document is null)
        {
            return;
        }

        CommitTextEdit(commit: false);
        var alive = _document.Objects.Where(Matches).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        _selected.RemoveWhere(id => !alive.Contains(id));
        Rebuild();
        _dirty = true;
        Flush();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>文档变了（切表面回来、撤销、外部导入）就整块重画；控件本身常驻。</summary>
    public void Rebuild()
    {
        _plane.Children.Clear();
        _overlay.Children.Clear();
        _visuals.Clear();
        _shapeVisuals.Clear();
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
            // 笔画由 InkCanvas 层负责；这里再画一遍就会出现两套描边
            if (item.Kind == CanvasObject.KindStroke)
            {
                continue;
            }

            if (item.Kind == CanvasObject.KindShape)
            {
                var shape = BuildShape(item);
                if (shape is not null)
                {
                    Panel.SetZIndex(shape, item.ZIndex);
                    _plane.Children.Add(shape);
                    _shapeVisuals[item.Id] = shape;
                }

                continue;
            }

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
        _selected.RemoveWhere(id => !_visuals.ContainsKey(id) && !_shapeVisuals.ContainsKey(id));
        RebuildInk();
        ApplySelectionVisuals();
        UpdateEmpty();
    }

    private bool Matches(CanvasObject item) =>
        string.Equals(CanvasSurfaces.Normalize(item.Surface), SurfaceId, StringComparison.OrdinalIgnoreCase);

    private void UpdateEmpty()
    {
        _empty.Visibility = ObjectCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- 笔迹层（plan §4.8：InkCanvas 只承载笔画，几何图元自绘）-------------

    /// <summary>笔迹工具时让 InkCanvas 接管指针，其余工具放行。</summary>
    private void UpdateInkLayer()
    {
        var ink = _tool == FreeformTool.Ink;
        _ink.IsHitTestVisible = ink;
        _ink.EditingMode = ink ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        if (ink)
        {
            var width = 3.0;
            var color = (FindResource("Accent") as SolidColorBrush)?.Color ?? Colors.White;
            _ink.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = color,
                Width = width,
                Height = width,
                FitToCurve = true,
                StylusTip = StylusTip.Ellipse,
            };
        }
    }

    /// <summary>从文档重建笔迹层：一笔一个对象，内容来自 <c>payload.path</c> 指向的 ISF。</summary>
    private void RebuildInk()
    {
        _ink.Strokes.Clear();
        _strokes.Clear();
        if (_document is null)
        {
            return;
        }

        foreach (var item in _document.Objects.Where(Matches).Where(o => o.Kind == CanvasObject.KindStroke))
        {
            var strokes = LoadStroke(item);
            if (strokes is null)
            {
                continue;
            }

            _strokes[item.Id] = strokes;
            _ink.Strokes.Add(strokes);
        }
    }

    private StrokeCollection? LoadStroke(CanvasObject item)
    {
        var relative = item.Get("path");
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var full = IOPath.IsPathRooted(relative)
            ? relative
            : IOPath.Combine(_inkDir ?? string.Empty, relative);
        if (!File.Exists(full))
        {
            AppLog.Warn($"[画布] 笔迹文件缺失：{full}");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(full);
            return new StrokeCollection(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            AppLog.Warn($"[画布] 笔迹文件读不出：{full}（{ex.Message}）");
            return null;
        }
    }

    private string? StrokePath(CanvasObject item) =>
        item.Get("path") is { Length: > 0 } relative
            ? (IOPath.IsPathRooted(relative) ? relative : IOPath.Combine(_inkDir ?? string.Empty, relative))
            : null;

    private void OnStrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e) => CommitStroke(e.Stroke);

    /// <summary>
    /// 把一笔落成模型 + ISF 文件。抽出来是为了让探针能不经真实触控笔就走到同一条路径
    /// （<c>spike/FreeformProbe</c> 通过 InternalsVisibleTo 访问）。
    /// </summary>
    internal void CommitStroke(Stroke stroke)
    {
        var document = _document;
        if (document is null || string.IsNullOrWhiteSpace(_inkDir))
        {
            return;
        }

        PushUndo();

        var id = document.NextObjectId();
        var bounds = stroke.GetBounds();
        var item = new CanvasObject
        {
            Id = id,
            Kind = CanvasObject.KindStroke,
            Surface = SurfaceId,
            X = Math.Max(0, bounds.X),
            Y = Math.Max(0, bounds.Y),
            W = Math.Max(CanvasObject.MinWidth, bounds.Width),
            H = Math.Max(CanvasObject.MinHeight, bounds.Height),
            ZIndex = NextZ(document),
        };
        item.Normalize();
        var fileName = id + ".isf";

        try
        {
            Directory.CreateDirectory(_inkDir);
            var single = new StrokeCollection { stroke };
            using var stream = File.Create(IOPath.Combine(_inkDir, fileName));
            single.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不出 ISF 就不要往文档里塞一个指向空文件的图元
            AppLog.Warn($"[画布] 笔迹保存失败：{ex.Message}");
            _ink.Strokes.Remove(stroke);
            return;
        }

        item.Payload["path"] = fileName;
        item.Payload["color"] = "accent";
        item.Payload["strokeWidth"] = "3";
        document.Objects.Add(item);
        _strokes[id] = new StrokeCollection { stroke };
        _ink.Strokes.Add(stroke);
        _dirty = true;
        Flush();
        UpdateEmpty();
        AppLog.Info($"[画布] 新增笔迹 {id} 顶点={stroke.StylusPoints.Count} 尺寸={item.W:0}x{item.H:0}");
    }

    private static int NextZ(CanvasDocument document) =>
        document.Objects.Count == 0 ? 1 : Math.Clamp(document.Objects.Max(o => o.ZIndex) + 1, 1, 999);

    // ---- 图形与文本创建 -----------------------------------------------------

    /// <summary>拖动式创建（矩形/线/箭头）：按下记起点，移动画预览，抬起落模型。</summary>
    private void BeginCreate(Point board, FreeformTool tool)
    {
        _creating = true;
        _createStart = ToCanvas(board);
        _createPreview = new Rectangle
        {
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _createPreview.SetResourceReference(Shape.StrokeProperty, "Accent");
        Canvas.SetLeft(_createPreview, _createStart.X);
        Canvas.SetTop(_createPreview, _createStart.Y);
        _plane.Children.Add(_createPreview);
        _ = tool;
    }

    private void UpdateCreate(Point board)
    {
        if (!_creating || _createPreview is null)
        {
            return;
        }

        var current = ToCanvas(board);
        var rect = new Rect(_createStart, current);
        Canvas.SetLeft(_createPreview, rect.X);
        Canvas.SetTop(_createPreview, rect.Y);
        _createPreview.Width = Math.Max(1, rect.Width);
        _createPreview.Height = Math.Max(1, rect.Height);
    }

    private void EndCreate(Point board)
    {
        var tool = _tool;
        var start = _createStart;
        var end = ToCanvas(board);
        _creating = false;
        if (_createPreview is not null)
        {
            _plane.Children.Remove(_createPreview);
            _createPreview = null;
        }

        var rect = new Rect(start, end);
        // 手抖点一下不该落图元：线类要求 6 px 以上的位移，矩形要求 8×8
        if (tool is FreeformTool.Line or FreeformTool.Arrow)
        {
            if (Math.Abs(end.X - start.X) < 6 && Math.Abs(end.Y - start.Y) < 6)
            {
                return;
            }
        }
        else if (rect.Width < 8 || rect.Height < 8)
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        PushUndo();
        var id = _document.NextObjectId();
        var item = new CanvasObject
        {
            Id = id,
            Kind = CanvasObject.KindShape,
            Surface = SurfaceId,
            X = Math.Max(0, rect.X),
            Y = Math.Max(0, rect.Y),
            W = Math.Max(CanvasObject.MinWidth, rect.Width),
            H = Math.Max(CanvasObject.MinHeight, rect.Height),
            ZIndex = NextZ(_document),
        };
        item.Normalize();
        item.Payload["shape"] = tool switch
        {
            FreeformTool.Rect => "rect",
            FreeformTool.Line => "line",
            _ => "arrow",
        };
        item.Payload["x1"] = start.X.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["y1"] = start.Y.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["x2"] = end.X.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["y2"] = end.Y.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["color"] = "accent";
        item.Payload["strokeWidth"] = "2";
        _document.Objects.Add(item);
        SelectOnly(id);
        Rebuild();
        _dirty = true;
        Flush();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>文本工具：点一下落一个文本框，并直接进入编辑（Enter 提交、Esc 取消）。</summary>
    private void CreateText(Point board)
    {
        if (_document is null)
        {
            return;
        }

        var canvas = ToCanvas(board);
        PushUndo();
        var id = _document.NextObjectId();
        var item = new CanvasObject
        {
            Id = id,
            Kind = CanvasObject.KindText,
            Surface = SurfaceId,
            X = Math.Max(0, canvas.X - 8),
            Y = Math.Max(0, canvas.Y - 10),
            W = 160,
            H = 40,
            ZIndex = NextZ(_document),
        };
        item.Normalize();
        item.Payload["text"] = "";
        item.Payload["color"] = "textPrimary";
        _document.Objects.Add(item);
        Rebuild();
        BeginTextEdit(item);
    }

    /// <summary>在画布上开一个与缩放同步的输入框（放在控件坐标里，用 zoom 换算尺寸）。</summary>
    public void BeginTextEdit(CanvasObject item)
    {
        CommitTextEdit(commit: true);
        _editingItem = item;

        var zoom = Math.Max(0.25, _scale.ScaleX);
        var topLeft = ToBoard(new Point(item.X, item.Y));
        _textEditor = new TextBox
        {
            Text = item.Get("text") ?? string.Empty,
            FontSize = 14 * zoom,
            Width = Math.Max(60, item.W * zoom),
            Height = Math.Max(24, item.H * zoom),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
        };
        _textEditor.SetResourceReference(TextBox.ForegroundProperty, "TextPrimary");
        _textEditor.SetResourceReference(TextBox.BackgroundProperty, "SurfaceCard");
        _textEditor.SetResourceReference(TextBox.CaretBrushProperty, "Accent");

        Children.Add(_textEditor);
        Canvas.SetLeft(_textEditor, topLeft.X);
        Canvas.SetTop(_textEditor, topLeft.Y);
        _textEditor.KeyDown += OnTextEditorKeyDown;
        _textEditor.LostKeyboardFocus += (_, _) => CommitTextEdit(commit: true);
        _textEditor.Focus();
        _textEditor.SelectAll();
    }

    /// <summary>双击图元进入编辑（文本编辑；其它类型 P2.1 再接）。</summary>
    public void BeginEdit(string id)
    {
        var item = _document?.Objects.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
        if (item is null)
        {
            return;
        }

        if (item.Kind == CanvasObject.KindText)
        {
            BeginTextEdit(item);
        }
    }

    private void OnTextEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            CommitTextEdit(commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitTextEdit(commit: false);
            e.Handled = true;
        }
    }

    /// <summary>提交/放弃文本编辑；空文本的对象会被删掉（避免留一个看不见的空图元）。</summary>
    public void CommitTextEdit(bool commit)
    {
        if (_textEditor is null || _editingItem is null)
        {
            return;
        }

        var editor = _textEditor;
        var item = _editingItem;
        _textEditor = null;
        _editingItem = null;
        editor.KeyDown -= OnTextEditorKeyDown;
        Children.Remove(editor);

        var text = commit ? editor.Text : null;
        if (commit)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _document?.Objects.RemoveAll(o => string.Equals(o.Id, item.Id, StringComparison.Ordinal));
                _selected.Remove(item.Id);
            }
            else
            {
                item.Payload["text"] = text;
            }

            Rebuild();
            _dirty = true;
            Flush();
            return;
        }

        // 取消：新建的空文本框整个拿掉，已有文本保持原样
        if (string.IsNullOrWhiteSpace(item.Get("text")))
        {
            _document?.Objects.RemoveAll(o => string.Equals(o.Id, item.Id, StringComparison.Ordinal));
            _selected.Remove(item.Id);
            Rebuild();
            _dirty = true;
            Flush();
        }
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

        foreach (var (id, shape) in _shapeVisuals)
        {
            var selected = _selected.Contains(id);
            shape.StrokeThickness = selected ? 3 : 2;
            shape.SetResourceReference(Shape.StrokeProperty, selected ? "Accent" : "BorderActive");
        }
    }

    /// <summary>删除选中图元（一笔一块撤销，ISF 文件一并清掉）。</summary>
    public void DeleteSelected()
    {
        var document = _document;
        if (document is null || _selected.Count == 0)
        {
            return;
        }

        PushUndo();
        foreach (var id in _selected.ToArray())
        {
            RemoveStrokeFile(id);
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

        var target = document.Objects.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
        if (target is null)
        {
            return;
        }

        PushUndo();
        RemoveStrokeFile(id);
        document.Objects.RemoveAll(o => string.Equals(o.Id, id, StringComparison.Ordinal));
        _selected.Remove(id);
        Rebuild();
        _dirty = true;
        Flush();
    }

    /// <summary>
    /// 插入一张图片（P1.3）：资产策略在 <see cref="ImageAssets"/>（sha256 去重、20 MB 上限、相对路径），
    /// 这里只决定落位与显示尺寸。图片不画边框——V-06 要求「像直接贴在画布上」。
    /// </summary>
    public CanvasObject? AddImageObject(string sourcePath, Point? canvasPoint = null)
    {
        var document = _document;
        if (document is null || string.IsNullOrWhiteSpace(_imagesDir))
        {
            return null;
        }

        var asset = ImageAssets.Import(sourcePath, _imagesDir);
        if (!asset.Imported || asset.RelativePath is null)
        {
            AppLog.Warn(asset.Error ?? "[图片] 导入失败");
            return null;
        }

        if (asset.Warning is not null)
        {
            AppLog.Warn(asset.Warning);
        }

        var (naturalWidth, naturalHeight) = ReadNaturalSize(asset.AbsolutePath!);
        const double defaultWidth = 320;
        double width = naturalWidth > 0 ? Math.Min(defaultWidth, naturalWidth) : defaultWidth;
        double height = naturalHeight > 0
            ? Math.Clamp(width * naturalHeight / Math.Max(1, naturalWidth), 80, 640)
            : width * 0.625;
        if (naturalWidth > 0 && naturalHeight > 0 && height > 480)
        {
            height = 480;
            width = height * naturalWidth / naturalHeight;
        }

        var point = canvasPoint ?? ToCanvas(new Point(ActualWidth / 2, ActualHeight / 2));
        PushUndo();
        var item = new CanvasObject
        {
            Id = document.NextObjectId(),
            Kind = CanvasObject.KindImage,
            Surface = SurfaceId,
            X = Math.Max(0, point.X - (width / 2)),
            Y = Math.Max(0, point.Y - (height / 2)),
            W = width,
            H = height,
            ZIndex = NextZ(document),
        };
        item.Normalize();
        item.Payload["path"] = asset.RelativePath;
        if (naturalWidth > 0)
        {
            item.Payload["naturalW"] = naturalWidth.ToString(CultureInfo.InvariantCulture);
            item.Payload["naturalH"] = naturalHeight.ToString(CultureInfo.InvariantCulture);
        }

        document.Objects.Add(item);
        SelectOnly(item.Id);
        _dirty = true;
        Rebuild();
        Flush();
        AppLog.Info($"[图片] 插入 {asset.RelativePath} 尺寸 {item.W:0}x{item.H:0}"
            + (asset.Deduplicated ? "（复用已有文件）" : string.Empty));
        return item;
    }

    /// <summary>剪贴板两形态（plan §5.4）：位图编码成临时 PNG，文件/路径按扩展名判定。</summary>
    public bool Paste()
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap)
            {
                string temporary = IOPath.Combine(
                    IOPath.GetTempPath(),
                    "wantodo-freeform-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        encoder.Save(stream);
                        stream.Flush(true);
                    }

                    return AddImageObject(temporary) is not null;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporary))
                        {
                            File.Delete(temporary);
                        }
                    }
                    catch (IOException)
                    {
                        // 临时文件删不掉不影响粘贴结果
                    }
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                foreach (var file in Clipboard.GetFileDropList())
                {
                    if (file is null || !ImageAssets.IsSupported(file))
                    {
                        continue;
                    }

                    if (AddImageObject(file) is not null)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn($"[图片] 粘贴失败：{ex.GetType().Name}: {ex.Message}");
        }

        return false;
    }

    /// <summary>删掉笔迹对象对应的 ISF；文件已被别处删掉也不报错。</summary>
    private void RemoveStrokeFile(string id)
    {
        var item = _document?.Objects.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
        if (item is null || item.Kind != CanvasObject.KindStroke)
        {
            return;
        }

        var path = StrokePath(item);
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 文件在别处被占用不算致命：模型里已经删了，留下的孤儿文件下次清理
            AppLog.Warn($"[画布] 笔迹文件删不掉：{path}（{ex.Message}）");
        }
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

        _lastBoard = board;
        CommitTextEdit(commit: true);
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
            switch (_tool)
            {
                case FreeformTool.Text:
                    CreateText(board);
                    break;
                case FreeformTool.Rect:
                case FreeformTool.Line:
                case FreeformTool.Arrow:
                    BeginCreate(board, _tool);
                    break;
                case FreeformTool.Image:
                    // 文件对话框归宿主管（控件不该自己弹窗）；落位点就是这次点击的位置
                    PendingImagePoint = board;
                    ImagePickRequested?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    // 画笔走 InkCanvas 的 StrokeCollected，不在这里建对象
                    AppLog.Info($"[画布] 工具 {FreeformToolCatalog.IdOf(_tool)} 的创建行为在 P1.3 接入");
                    break;
            }

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
            _gestureUndo = false;
            _pendingUndo = Snapshot();
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
        _lastBoard = board;
        if (_creating)
        {
            UpdateCreate(board);
            return;
        }

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
            if (!_gestureUndo && (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5))
            {
                // 手势开始前的快照在 PointerDown 就抓好了：一次拖动只压一份，
                // 撤销一步回到拖动前，而不是回到上一帧。
                if (_pendingUndo is not null)
                {
                    _undo.Push(SurfaceId, _pendingUndo);
                }

                _pendingUndo = null;
                _gestureUndo = true;
            }

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
        if (_creating)
        {
            EndCreate(_lastBoard);
            return;
        }

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

        if (e.ClickCount == 2 && _tool == FreeformTool.Select)
        {
            var hit = HitTest(ToCanvas(e.GetPosition(this)));
            if (hit is not null)
            {
                BeginEdit(hit.Id);
                e.Handled = true;
                return;
            }
        }

        Focus();
        PointerDown(e.GetPosition(this), Keyboard.Modifiers);
        if (_panning || _moving || _marquee || _creating)
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
            case CanvasObject.KindStroke:
                // 笔画在 InkCanvas 层，这里不要占位框
                return null;
            default:
                return MissingItem(item);
        }
    }

    /// <summary>
    /// 图形图元按绝对画布坐标画（不套 <see cref="Wrap"/>）：线段不能用一个矩形框代表，
    /// 端点与走向必须来自 <c>payload</c>。颜色走令牌名，换色板自动跟随。
    /// </summary>
    private Shape? BuildShape(CanvasObject item)
    {
        var shape = item.Get("shape") ?? "rect";
        var x1 = Parse(item.Get("x1"), item.X);
        var y1 = Parse(item.Get("y1"), item.Y);
        var x2 = Parse(item.Get("x2"), item.X + item.W);
        var y2 = Parse(item.Get("y2"), item.Y + item.H);
        var width = Math.Max(1, Math.Abs(x2 - x1));
        var height = Math.Max(1, Math.Abs(y2 - y1));

        Shape element;
        switch (shape)
        {
            case "line":
                element = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
                break;
            case "arrow":
                // 箭头必须看得出朝向：线 + 两条回折的头部（线本身没有头，两者不能画成一样）
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(new Point(x1, y1), false, false);
                    context.LineTo(new Point(x2, y2), true, false);
                    var angle = Math.Atan2(y2 - y1, x2 - x1);
                    const double headLength = 12;
                    const double headSpread = Math.PI / 7;
                    context.BeginFigure(new Point(x2, y2), false, false);
                    context.LineTo(
                        new Point(x2 - (headLength * Math.Cos(angle - headSpread)), y2 - (headLength * Math.Sin(angle - headSpread))),
                        true,
                        false);
                    context.BeginFigure(new Point(x2, y2), false, false);
                    context.LineTo(
                        new Point(x2 - (headLength * Math.Cos(angle + headSpread)), y2 - (headLength * Math.Sin(angle + headSpread))),
                        true,
                        false);
                }

                geometry.Freeze();
                element = new System.Windows.Shapes.Path { Data = geometry };
                break;
            default:
                element = new Rectangle
                {
                    Width = width,
                    Height = height,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = Brushes.Transparent,
                };
                Canvas.SetLeft(element, Math.Min(x1, x2));
                Canvas.SetTop(element, Math.Min(y1, y2));
                break;
        }

        element.StrokeThickness = 2;
        element.SetResourceReference(Shape.StrokeProperty, "BorderActive");
        element.IsHitTestVisible = false;
        return element;
    }

    private static double Parse(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static (int Width, int Height) ReadNaturalSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return (0, 0);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && !(_textEditor is { IsKeyboardFocusWithin: true }))
        {
            if (Paste())
            {
                e.Handled = true;
            }
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
