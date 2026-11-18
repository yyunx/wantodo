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

    /// <summary>墨层能承载的坐标范围（含负坐标，见构造函数里的 +8000 平移）。</summary>
    private const double InkCanvasExtent = 16000;

    private const double InkCanvasPadding = InkCanvasExtent / 2;

    private readonly Canvas _inkHost = new() { ClipToBounds = false, IsHitTestVisible = false };
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

    /// <summary>笔迹静态化阈值（plan §4.8：>500 笔或 >2000 顶点转位图）。</summary>
    private const int FlattenStrokeLimit = 500;

    private const int FlattenVertexLimit = 2000;

    /// <summary>笔迹已静态化成位图；此后新笔迹仍进 InkCanvas，攒够再折一次。</summary>
    private bool _inkFlattened;

    private Image? _inkStatic;

    private readonly TextBlock _empty = new()
    {
        Text = "还没有内容 · 选上面的笔 / 矩形 / 文本，或直接 Ctrl+V 贴一张图",
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
    private Shape? _createPreview;

    // 笔与橡皮自己收点：InkCanvas 一旦被 RenderTransform 平移，命中区跟着走，
    // 画布上半截会落到「没反应」的死区里。所以墨层只当显示器，输入全部走 Pointer*。
    private StylusPointCollection? _strokePoints;
    private Stroke? _stroke;
    private bool _erasing;

    /// <summary>画笔颜色；<see cref="Colors.Transparent"/> 表示跟随当前色板的强调色（C8）。</summary>
    public Color StrokeColor { get; set; } = Colors.Transparent;

    /// <summary>画笔粗细（DIP）。</summary>
    public double StrokeWidth { get; set; } = 3;

    /// <summary>当前画的颜色键（`accent` 或 `#RRGGBB`）；笔和图形共用（C8）。</summary>
    public string ColorKey { get; private set; } = "accent";

    /// <summary>
    /// 用户是否主动选了「跟随色板强调色」。默认的 `accent` 只是一次快照，
    /// 不然换主题或换背景色会把正在用的画笔颜色一起改掉。
    /// </summary>
    private bool _followPalette;

    /// <summary>图形线宽（DIP）；跟画笔共用一个粗细档位。</summary>
    public double ShapeWidth { get; private set; } = 2;

    /// <summary>设置颜色：`accent` 表示跟随色板强调色，其余按 #RRGGBB 解析。</summary>
    public void SetColorKey(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "accent" : key.Trim();
        var next = normalized.Equals("accent", StringComparison.OrdinalIgnoreCase) ? "accent" : normalized;
        PinPaletteColorIfNeeded();
        if (next == ColorKey)
        {
            // 同一个颜色重复设置：工具栏那支笔的颜色显示与开关状态不该被无意义地重置。
            return;
        }

        ColorKey = next;
        _followPalette = ColorKey == "accent";
        StrokeColor = ParseColor(ColorKey);
        StrokeWidth = Math.Clamp(StrokeWidth, 1, 24);
        ShapeWidth = Math.Clamp(StrokeWidth, 1, 16);
        UpdateInkLayer();
        StrokeStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>设置粗细（1–24 DIP），图形线宽跟着走同一档。</summary>
    public void SetStrokeWidth(double width)
    {
        var next = Math.Clamp(width, 1, 24);
        PinPaletteColorIfNeeded();
        if (Math.Abs(next - StrokeWidth) < 0.001)
        {
            return;
        }

        StrokeWidth = next;
        ShapeWidth = Math.Clamp(StrokeWidth, 1, 16);
        UpdateInkLayer();
        StrokeStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Color ParseColor(string key)
    {
        if (key.Equals("accent", StringComparison.OrdinalIgnoreCase))
        {
            return Colors.Transparent;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(key);
        }
        catch (FormatException)
        {
            return Colors.Transparent;
        }
    }
    private bool _strokeDashed;

    /// <summary>新建的线/箭头/矩形/圆是否走虚线（工具条 C7 的线型开关）。</summary>
    public bool StrokeDashed
    {
        get => _strokeDashed;
        set => _strokeDashed = value;
    }

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
        // InkCanvas 默认 ClipToBounds=true，会把自己的笔画裁到元素边界的局部 [0,w]×[0,h] 里；
        // 而画布坐标允许为负（视口 offset 让原点落在可视区下方），RenderTransform 又不改变裁剪用
        // 的局部坐标——只把内容往正区间挪是白挪的，canvas y<0 的笔迹一个像素都画不出来，
        // 这就是「笔只有下半截能画」的根因。关掉这层裁剪，负坐标才真正落进可视区。
        _ink.ClipToBounds = false;
        _ink.Width = InkCanvasExtent;
        _ink.Height = InkCanvasExtent;
        _ink.HorizontalAlignment = HorizontalAlignment.Left;
        _ink.VerticalAlignment = VerticalAlignment.Top;
        _ink.RenderTransform = new TransformGroup
        {
            Children =
            {
                new TranslateTransform(InkCanvasPadding, InkCanvasPadding),
                _scale,
                _offset,
            },
        };
        _ink.Background = Brushes.Transparent;
        _ink.EditingMode = InkCanvasEditingMode.None;
        _ink.IsHitTestVisible = false;
        _ink.UseCustomCursor = true;
        _ink.Cursor = Cursors.Pen;
        _ink.StrokeCollected += OnStrokeCollected;
        _inkHost.IsHitTestVisible = false;
        _inkHost.Children.Add(_ink);

        Children.Add(_plane);
        Children.Add(_inkHost);
        Children.Add(_overlay);
        Children.Add(_empty);

        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        UseLayoutRounding = true;

        // 自由表面右键：粘图 / 全选 / 删除所选（plan §3.3「右键菜单」行；图元本身无铬，菜单挂在画布上）
        var menu = new ContextMenu();
        var paste = new MenuItem { Header = "粘贴图片" };
        paste.Click += (_, _) => Paste();
        var selectAll = new MenuItem { Header = "全选" };
        selectAll.Click += (_, _) => SelectAll();
        var delete = new MenuItem { Header = "删除所选" };
        delete.Click += (_, _) => DeleteSelected();
        menu.Items.Add(paste);
        menu.Items.Add(selectAll);
        menu.Items.Add(delete);
        menu.Opened += (_, _) => delete.IsEnabled = _selected.Count > 0;
        ContextMenu = menu;

        // 换主题/换背景色之后必须重画一次：图元里的画刷是建的时候用 FindResource 抓的快照，
        // 不重建就一直停在旧颜色上（用户看到的是「换了背景，图元/卡片要等下一次操作才适配」）。
        Appearance.ThemeService.Applied += OnThemeApplied;
    }

    private void OnThemeApplied()
    {
        if (!IsLoaded)
        {
            return;
        }

        Rebuild();
        UpdateInkLayer();
        ApplyCursor();
    }

    /// <summary>右键菜单用：把当前表面的图元全选（笔迹层不算，笔迹只能单点选）。</summary>
    public void SelectAll()
    {
        if (Document is null)
        {
            return;
        }

        _selected.Clear();
        foreach (var item in Document.Objects)
        {
            if (string.Equals(CanvasSurfaces.Normalize(item.Surface), SurfaceId, StringComparison.OrdinalIgnoreCase))
            {
                _selected.Add(item.Id);
            }
        }

        ApplySelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// 表面被切回来时重放一次「工具层」状态。
    /// </summary>
    /// <remarks>
    /// 笔迹层是否吃鼠标只由 <see cref="UpdateInkLayer"/> 决定，而它只在 <see cref="Tool"/> 变化时跑。
    /// 之前切到结构化再切回来、工具仍是「笔」时，命中状态是旧的（用户看到的就是「笔点了没反应」），
    /// 所以切面时强制同步一次。
    /// </remarks>
    public void SyncToolLayer() => UpdateInkLayer();

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

    /// <summary>探针用：笔迹层当前是否吃鼠标（B2 的回归点）。</summary>
    /// <summary>笔工具下本控件自己收点（探针与真机同一条路径）。</summary>
    internal bool InkLayerActiveForTest => _tool == FreeformTool.Ink;

    public double Zoom => _document?.ViewportOf(SurfaceId).Zoom ?? 1.0;

    public IReadOnlyCollection<string> Selected => _selected;

    public int ObjectCount => _document?.Objects.Count(o => Matches(o)) ?? 0;

    /// <summary>InkCanvas 里当前的笔画数，探针用来确认「模型有笔迹」和「画布上真看得见」是一回事。</summary>
    internal int InkStrokeCount => _ink.Strokes.Count;

    /// <summary>某个图元当前的视觉元素（探针用来确认图片真的没外框）。</summary>
    internal FrameworkElement? VisualOf(string id) => _visuals.TryGetValue(id, out var element) ? element : null;

    internal Shape? ShapeVisualOf(string id) => _shapeVisuals.TryGetValue(id, out var shape) ? shape : null;

    /// <summary>拖拽中的预览图元（C6：按下就该看见最终图形，而不是一个虚线框）。</summary>
    internal Shape? CreatePreviewForTest => _createPreview;

    /// <summary>缩放/平移结束后触发，供状态行刷新（不参与保存）。</summary>
    public event EventHandler? ViewportChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// 画笔颜色或粗细变了。工具条第「色」按钮上的色块靠它显示「现在手里是什么颜色」，
    /// 包括第一次落笔把 accent 快照成具体色值时的那一次变化。
    /// </summary>
    public event EventHandler? StrokeStyleChanged;

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
        AppLog.Info(
            $"[自由表面] rebuild 图元={ObjectCount} 可视={_plane.Children.Count} 笔迹={_ink.Strokes.Count} " +
            $"缩放={viewport.Zoom:0.##} 偏移=({viewport.OffsetX:0.#},{viewport.OffsetY:0.#}) " +
            $"控件={ActualWidth:0}x{ActualHeight:0}");
    }

    private bool Matches(CanvasObject item) =>
        string.Equals(CanvasSurfaces.Normalize(item.Surface), SurfaceId, StringComparison.OrdinalIgnoreCase);

    private void UpdateEmpty()
    {
        _empty.Visibility = ObjectCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- 笔迹层（plan §4.8：InkCanvas 只承载笔画，几何图元自绘）-------------

    /// <summary>
    /// 墨层只负责把笔画画出来，命中永远交给本控件（<see cref="PointerDown"/> 收点）。
    /// 这里只负责让笔尖颜色/粗细跟上当前选择。
    /// </summary>
    private void UpdateInkLayer()
    {
        PinPaletteColorIfNeeded();
        _ink.IsHitTestVisible = false;
        _ink.EditingMode = InkCanvasEditingMode.None;
        var width = Math.Clamp(StrokeWidth, 1, 24);
        var color = ResolveStrokeColor();
        _ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = color,
            Width = width,
            Height = width,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse,
        };
    }

    /// <summary>
    /// 默认笔色取「当时的强调色」快照，之后不再跟着色板走：
    /// 换主题/换背景色只该改背景，不该把已经在手里的画笔颜色也改掉。
    /// 只有用户主动点了「跟随色板强调色」（<see cref="_followPalette"/>）才继续跟。
    /// </summary>
    private void PinPaletteColorIfNeeded()
    {
        if (_followPalette || StrokeColor != Colors.Transparent)
        {
            return;
        }

        if (TryFindResource("Accent") is not SolidColorBrush brush)
        {
            return;
        }

        StrokeColor = brush.Color;
        ColorKey = $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        StrokeStyleChanged?.Invoke(this, EventArgs.Empty);
    }

    private Color ResolveStrokeColor()
    {
        if (StrokeColor != Colors.Transparent)
        {
            return StrokeColor;
        }

        return (FindResource("Accent") as SolidColorBrush)?.Color ?? Colors.White;
    }

    /// <summary>从文档重建笔迹层：一笔一个对象，内容来自 <c>payload.path</c> 指向的 ISF。</summary>
    private void RebuildInk()
    {
        _ink.Strokes.Clear();
        _strokes.Clear();
        RemoveStaticInk();
        if (_document is null)
        {
            return;
        }

        var repaired = 0;
        foreach (var item in _document.Objects.Where(Matches).Where(o => o.Kind == CanvasObject.KindStroke))
        {
            var strokes = LoadStroke(item);
            if (strokes is null)
            {
                continue;
            }

            // 自愈：旧版本把笔迹的模型矩形 Math.Max(0,…) 夹到了 0，和 ISF 里的真实位置错开，
            // 橡皮/框选/选中框全按错的矩形命中。ISF 是权威，读出来后顺手把矩形校正回去。
            if (SyncStrokeBounds(item, strokes))
            {
                repaired++;
            }

            _strokes[item.Id] = strokes;
            if (!_inkFlattened)
            {
                _ink.Strokes.Add(strokes);
            }
        }

        if (repaired > 0)
        {
            AppLog.Info($"[画布] 笔迹矩形自愈 {repaired} 笔（模型矩形被旧版本夹到 0）");
            _dirty = true;
            Flush();
        }

        if (_inkFlattened)
        {
            RenderStaticInk(_strokes.Values);
        }
        else
        {
            MaybeFlattenInk();
        }
    }

    /// <summary>
    /// 笔迹静态化（plan §4.8 / V-12）：够多时把整层笔迹画成一张位图，
    /// 之后 InkCanvas 只承载新笔画，老笔画不再逐条走渲染管线。
    /// </summary>
    internal void FlattenInk()
    {
        if (_inkFlattened || _ink.Strokes.Count == 0)
        {
            return;
        }

        var strokes = _ink.Strokes.ToList();
        _ink.Strokes.Clear();
        _inkFlattened = true;
        RenderStaticInk(new[] { new StrokeCollection(strokes) });
        AppLog.Info($"[画布] 笔迹静态化：{strokes.Count} 笔 → 位图 {_inkStatic?.Width:0}×{_inkStatic?.Height:0}");
    }

    /// <summary>超过 500 笔或 2000 顶点时折一次（每次提交笔迹后调用）。</summary>
    private void MaybeFlattenInk()
    {
        if (_inkFlattened)
        {
            return;
        }

        var vertices = _ink.Strokes.Sum(stroke => stroke.StylusPoints.Count);
        if (_ink.Strokes.Count > FlattenStrokeLimit || vertices > FlattenVertexLimit)
        {
            FlattenInk();
        }
    }

    private void RenderStaticInk(IEnumerable<StrokeCollection> collections)
    {
        RemoveStaticInk();
        var strokes = collections.SelectMany(c => c).ToList();
        if (strokes.Count == 0)
        {
            return;
        }

        Rect bounds = Rect.Empty;
        foreach (var stroke in strokes)
        {
            bounds = bounds.IsEmpty ? stroke.GetBounds() : Rect.Union(bounds, stroke.GetBounds());
        }

        bounds.Inflate(8, 8);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            foreach (var stroke in strokes)
            {
                stroke.Draw(dc);
            }

            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var image = new Image
        {
            Source = bitmap,
            Width = bounds.Width,
            Height = bounds.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(image, bounds.X);
        Canvas.SetTop(image, bounds.Y);
        _inkStatic = image;
        _plane.Children.Add(image);
    }

    private void RemoveStaticInk()
    {
        if (_inkStatic is null)
        {
            return;
        }

        _plane.Children.Remove(_inkStatic);
        _inkStatic = null;
    }

    /// <summary>探针钩子：确认静态化层真的建了、InkCanvas 已不在承载老笔画。</summary>
    internal bool HasStaticInkLayer => _inkStatic is not null;

    internal static int FlattenStrokeLimitForTest => FlattenStrokeLimit;

    internal static int FlattenVertexLimitForTest => FlattenVertexLimit;

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

    /// <summary>
    /// 让笔迹对象的模型矩形与 ISF 里的真实外框一致（ISF 是权威）。
    /// 返回是否真的改过；改过就说明这条记录来自会夹到 0 的旧版本。
    /// </summary>
    private static bool SyncStrokeBounds(CanvasObject item, StrokeCollection strokes)
    {
        if (strokes.Count == 0)
        {
            return false;
        }

        var bounds = Rect.Empty;
        foreach (var stroke in strokes)
        {
            var b = stroke.GetBounds();
            bounds = bounds.IsEmpty ? b : Rect.Union(bounds, b);
        }

        if (bounds.IsEmpty)
        {
            return false;
        }

        var w = Math.Max(CanvasObject.MinWidth, bounds.Width);
        var h = Math.Max(CanvasObject.MinHeight, bounds.Height);
        if (Math.Abs(item.X - bounds.X) < 0.5 && Math.Abs(item.Y - bounds.Y) < 0.5 &&
            Math.Abs(item.W - w) < 0.5 && Math.Abs(item.H - h) < 0.5)
        {
            return false;
        }

        item.X = bounds.X;
        item.Y = bounds.Y;
        item.W = w;
        item.H = h;
        item.Normalize();
        return true;
    }

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
            // 画布是「原点可落在可视区中央」的无限平面（FitToView 也居中），坐标允许为负；
            // 曾经这里 Math.Max(0,…) 把负坐标夹到 0，模型矩形就和笔迹实际位置错开，
            // 橡皮/框选/拖动全按错的矩形命中——上半截的笔迹既画不出来、擦不掉。
            X = bounds.X,
            Y = bounds.Y,
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
        item.Payload["color"] = ColorKey;
        item.Payload["strokeWidth"] = StrokeWidth.ToString("0.##", CultureInfo.InvariantCulture);
        document.Objects.Add(item);
        _strokes[id] = new StrokeCollection { stroke };
        // InkCanvas 在 StrokeCollected 前已经把笔画放进自己的集合；重复加会抛
        // ArgumentException（duplicate stroke），把后面的落盘与重画全部中断。
        if (!_ink.Strokes.Contains(stroke))
        {
            _ink.Strokes.Add(stroke);
        }

        _dirty = true;
        Flush();
        UpdateEmpty();
        MaybeFlattenInk();
        AppLog.Info($"[画布] 新增笔迹 {id} 顶点={stroke.StylusPoints.Count} 尺寸={item.W:0}x{item.H:0} 已静态化={_inkFlattened}");
    }

    private static int NextZ(CanvasDocument document) =>
        document.Objects.Count == 0 ? 1 : Math.Clamp(document.Objects.Max(o => o.ZIndex) + 1, 1, 999);

    // ---- 画笔与橡皮（自己收点，不走 InkCanvas 的输入）------------------------

    /// <summary>按下：起一个新笔画，先挂在墨层上实时显示，抬手再落模型。</summary>
    private void BeginStroke(Point canvas)
    {
        PinPaletteColorIfNeeded();
        _strokePoints = new StylusPointCollection { new StylusPoint(canvas.X, canvas.Y) };
        var width = Math.Clamp(StrokeWidth, 1, 24);
        var attributes = new DrawingAttributes
        {
            Color = ResolveStrokeColor(),
            Width = width,
            Height = width,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse,
        };
        _stroke = new Stroke(_strokePoints, attributes);
        _ink.Strokes.Add(_stroke);
    }

    private void AddStrokePoint(Point canvas)
    {
        if (_strokePoints is null)
        {
            return;
        }

        var last = _strokePoints[_strokePoints.Count - 1];
        // 太密的点没有信息量，还会让 ISF 变大
        if (Math.Abs(last.X - canvas.X) < 0.6 && Math.Abs(last.Y - canvas.Y) < 0.6)
        {
            return;
        }

        _strokePoints.Add(new StylusPoint(canvas.X, canvas.Y));
    }

    private void EndStroke()
    {
        var stroke = _stroke;
        _stroke = null;
        _strokePoints = null;
        if (stroke is null)
        {
            return;
        }

        if (stroke.StylusPoints.Count < 2)
        {
            // 点一下不留一个点：这是「没画」，不是一笔
            _ink.Strokes.Remove(stroke);
            return;
        }

        CommitStroke(stroke);
    }

    /// <summary>橡皮：按住拖过就连续擦（C5），按画布坐标命中。</summary>
    /// <summary>橡皮判定半径（屏幕 DIP）；画布单位按当前缩放换算。</summary>
    private const double EraserTolerance = 12;

    private void EraseAt(Point canvas)
    {
        var tolerance = EraserTolerance / Math.Max(0.0001, _scale.ScaleX);
        if (HitTest(canvas, tolerance) is { } target)
        {
            DeleteObject(target.Id);
        }
    }

    // ---- 图形与文本创建 -----------------------------------------------------

    /// <summary>拖动式创建（矩形/线/箭头）：按下记起点，移动画预览，抬起落模型。</summary>
    private void BeginCreate(Point board, FreeformTool tool)
    {
        PinPaletteColorIfNeeded();
        _creating = true;
        _createStart = ToCanvas(board);
        var shape = ShapeName(tool);
        var preview = NewShapeVisual(shape);
        preview.StrokeThickness = Math.Clamp(ShapeWidth, 1, 16);
        ApplyStrokeColor(preview, ColorKey);
        preview.IsHitTestVisible = false;
        if (_strokeDashed)
        {
            preview.StrokeDashArray = new DoubleCollection { 4, 3 };
        }

        // C6：预览就是最终图形本身（箭头按下即见箭头），不是外包一个虚线框。
        PositionShape(preview, shape, _createStart.X, _createStart.Y, _createStart.X + 1, _createStart.Y + 1);
        _plane.Children.Add(preview);
        _createPreview = preview;
    }

    private void UpdateCreate(Point board)
    {
        if (!_creating || _createPreview is null)
        {
            return;
        }

        var current = ToCanvas(board);
        PositionShape(_createPreview, ShapeName(_tool), _createStart.X, _createStart.Y, current.X, current.Y);
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
            X = rect.X,
            Y = rect.Y,
            W = Math.Max(CanvasObject.MinWidth, rect.Width),
            H = Math.Max(CanvasObject.MinHeight, rect.Height),
            ZIndex = NextZ(_document),
        };
        item.Normalize();
        item.Payload["shape"] = tool switch
        {
            FreeformTool.Rect => "rect",
            FreeformTool.Circle => "circle",
            FreeformTool.Line => "line",
            _ => "arrow",
        };
        item.Payload["x1"] = start.X.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["y1"] = start.Y.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["x2"] = end.X.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["y2"] = end.Y.ToString("0.##", CultureInfo.InvariantCulture);
        item.Payload["color"] = ColorKey;
        item.Payload["strokeWidth"] = ShapeWidth.ToString("0.##", CultureInfo.InvariantCulture);
        if (_strokeDashed)
        {
            item.Payload["dash"] = "1";
        }
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
            X = canvas.X - 8,
            Y = canvas.Y - 10,
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
        // 墨层用「+半幅平移 → 缩放 → 平移」表示画布坐标；元素本身要反向挪半个缩放后的幅面，
        // 这样画布坐标 0 才落在 offset 处，负坐标也不会被 InkCanvas 的边界裁掉。
        Canvas.SetLeft(_ink, -InkCanvasPadding * _scale.ScaleX);
        Canvas.SetTop(_ink, -InkCanvasPadding * _scale.ScaleX);
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

    /// <summary>
    /// 画布坐标下点中了哪个图元（取 Z 最高的那个）。<paramref name="tolerance"/>
    /// 是画布单位的外扩量：橡皮需要「差一点也算擦到」，否则细线几乎点不中。
    /// </summary>
    private CanvasObject? HitTest(Point canvasPoint, double tolerance = 0)
    {
        var document = _document;
        if (document is null)
        {
            return null;
        }

        var pad = Math.Max(0, tolerance);
        CanvasObject? hit = null;
        foreach (var item in document.Objects.Where(Matches))
        {
            if (canvasPoint.X >= item.X - pad && canvasPoint.X <= item.X + item.W + pad &&
                canvasPoint.Y >= item.Y - pad && canvasPoint.Y <= item.Y + item.H + pad)
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
            var item = _document?.Objects.FirstOrDefault(o => o.Id == id);
            // 选中只加粗，颜色回落到图元自己的色（C8）：选中不该把红色变成强调色。
            shape.StrokeThickness = Math.Clamp(Parse(item?.Get("strokeWidth"), 2), 1, 16) + (selected ? 1 : 0);
            ApplyStrokeColor(shape, item?.Get("color"));
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
            X = point.X - (width / 2),
            Y = point.Y - (height / 2),
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
            _erasing = true;
            EraseAt(canvas);
            return;
        }

        if (_tool == FreeformTool.Ink)
        {
            BeginStroke(canvas);
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
                case FreeformTool.Circle:
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
        if (_strokePoints is not null)
        {
            AddStrokePoint(ToCanvas(board));
            return;
        }

        if (_erasing)
        {
            EraseAt(ToCanvas(board));
            return;
        }

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
        if (_strokePoints is not null)
        {
            EndStroke();
            return;
        }

        if (_erasing)
        {
            _erasing = false;
            return;
        }

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
        if (_panning || _moving || _marquee || _creating || _strokePoints is not null || _erasing)
        {
            var ok = CaptureMouse();
            AppLog.Info($"[指针] 画板捕获 {ok} 手势={GestureName()}");
        }

        e.Handled = true;
    }

    private string GestureName()
    {
        if (_strokePoints is not null) return "ink";
        if (_erasing) return "eraser";
        if (_creating) return "create";
        if (_panning) return "pan";
        if (_moving) return "move";
        if (_marquee) return "marquee";
        return "none";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerMove(e.GetPosition(this));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        AppLog.Info($"[指针] 画板丢失捕获 手势={GestureName()}");
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        try
        {
            PointerUp();
        }
        finally
        {
            // 任何一次抬手都放掉捕获：画完图形/笔迹却还攥着鼠标，工具栏就再也点不动了。
            // 放进 finally 是必须的——UI 线程异常被 App 的 DispatcherUnhandledException 吞掉，
            // 一旦 PointerUp 中途抛出，下面这行就会被跳过，画板从此吃掉面板上所有点击。
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
                AppLog.Info("[指针] 画板释放捕获");
            }

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
        // 每个工具一个光标：笔是笔尖、橡皮是橡皮、缩放是放大镜，别一律十字。
        if (_panning || _spaceHeld || _tool == FreeformTool.Pan)
        {
            Cursor = Cursors.Hand;
            return;
        }

        Cursor = _tool switch
        {
            FreeformTool.Text => Cursors.IBeam,
            FreeformTool.Select => Cursors.Arrow,
            FreeformTool.Ink => ToolCursors.Pen,
            FreeformTool.Eraser => ToolCursors.Eraser,
            FreeformTool.Zoom => ToolCursors.Zoom,
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

    /// <summary>工具 → <c>payload.shape</c> 的取值（矩形/圆/线/箭头）。</summary>
    private static string ShapeName(FreeformTool tool) => tool switch
    {
        FreeformTool.Line => "line",
        FreeformTool.Arrow => "arrow",
        FreeformTool.Circle => "circle",
        _ => "rect",
    };

    private static Shape NewShapeVisual(string shape) => shape switch
    {
        "line" => new Line(),
        "arrow" => new System.Windows.Shapes.Path(),
        "circle" => new Ellipse { Fill = Brushes.Transparent },
        _ => new Rectangle { Fill = Brushes.Transparent, RadiusX = 4, RadiusY = 4 },
    };

    /// <summary>把 (x1,y1)-(x2,y2) 落到具体图形上；预览与最终图元共用这一段。</summary>
    private static void PositionShape(Shape element, string shape, double x1, double y1, double x2, double y2)
    {
        if (element is Line line)
        {
            line.X1 = x1;
            line.Y1 = y1;
            line.X2 = x2;
            line.Y2 = y2;
            return;
        }

        if (element is System.Windows.Shapes.Path path)
        {
            path.Data = BuildArrowGeometry(x1, y1, x2, y2);
            return;
        }

        element.Width = Math.Max(1, Math.Abs(x2 - x1));
        element.Height = Math.Max(1, Math.Abs(y2 - y1));
        Canvas.SetLeft(element, Math.Min(x1, x2));
        Canvas.SetTop(element, Math.Min(y1, y2));
    }

    /// <summary>箭头必须看得出朝向：线 + 两条回折的头部（线本身没有头，两者不能画成一样）。</summary>
    private static StreamGeometry BuildArrowGeometry(double x1, double y1, double x2, double y2)
    {
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
        return geometry;
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

        var element = NewShapeVisual(shape);
        PositionShape(element, shape, x1, y1, x2, y2);

        element.StrokeThickness = Math.Clamp(Parse(item.Get("strokeWidth"), 2), 1, 16);
        ApplyStrokeColor(element, item.Get("color"));
        if (string.Equals(item.Get("dash"), "1", StringComparison.Ordinal))
        {
            element.StrokeDashArray = new DoubleCollection { 4, 3 };
        }

        element.IsHitTestVisible = false;
        return element;
    }

    /// <summary>图形描边：`accent` 跟随色板（DynamicResource），自定义色存 #RRGGBB。</summary>
    private void ApplyStrokeColor(Shape element, string? key)
    {
        var effective = string.IsNullOrWhiteSpace(key) ? "accent" : key.Trim();
        if (effective.Equals("accent", StringComparison.OrdinalIgnoreCase))
        {
            element.SetResourceReference(Shape.StrokeProperty, "Accent");
            return;
        }

        var color = ParseColor(effective);
        element.Stroke = color == Colors.Transparent
            ? new SolidColorBrush(Colors.White)
            : new SolidColorBrush(color);
    }

    private static double Parse(string? text, double fallback) =>        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

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
