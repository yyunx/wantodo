using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wantodo.App.Appearance;
using Wantodo.App.Dialogs;
using Wantodo.App.Interop;
using Wantodo.App.Storage;
using Wantodo.App.ViewModels;
using Wantodo.Core;

namespace Wantodo.App.Windows;

/// <summary>
/// 顶边下拉面板（形态 A）：与主窗口共用同一个 <see cref="BoardViewModel"/>，
/// 所以两边看到的是同一份数据，不存在互相覆盖。
/// 收起时只剩 12 DIP 高的一条把手；按住把手往下拖，超过记忆高度的 40% 展开。
/// Phase 19：展开尺寸按「主屏工作区 × 记忆比例」算，左右下三边可拖缩放（顶边 12 DIP 仍归把手）。
/// </summary>
public partial class TopPanelWindow : Window
{
    private const double CollapsedHeight = 12;

    private readonly BoardViewModel _viewModel;
    private bool _dragging;
    private double _dragStartHeight;
    private int _dragStartY;
    private int _persistedDuringDrag;
    private int _moveLogCount;
    private int _lastScreenY;
    private FreeformTool _tool = FreeformTool.Select;

    private bool _resizing;
    private PanelEdge _resizeEdge;
    private PanelRect _resizeStart;
    private double _resizeStartX;
    private double _resizeStartY;
    private double _dpiScaleX = 1;
    private double _dpiScaleY = 1;

    public TopPanelWindow(BoardViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // Phase 20：面板与主窗口共用同一份布局渲染（BoardLayoutHost），只是叶子模板更紧凑。
        LayoutHost.PaneTemplate = (DataTemplate)FindResource("PanelPaneTemplate");
        LayoutHost.Attach(viewModel.Model, viewModel.Columns);
        LayoutHost.SaveRequested += (_, _) => viewModel.RequestSave();
        viewModel.Columns.CollectionChanged += OnColumnsChanged;
        ColumnsScroller.SizeChanged += OnSizeChangedFit;

        // 0.7.0：画布住 data.json，写盘走 Board 的合并保存（600 ms 去抖），不再有 canvas.json。
        CanvasBoard.Attach(viewModel.Model.Canvas, PersistCanvas, DataRootService.Paths.CanvasImagesDir);
        FreeformBoard.Attach(
            viewModel.Model.Canvas,
            () => PersistCanvas(viewModel.Model.Canvas),
            DataRootService.Paths.CanvasImagesDir,
            DataRootService.Paths.CanvasInkDir);
        CanvasBoard.StatusChanged += (_, _) => UpdateCanvasStatus();
        FreeformBoard.ViewportChanged += (_, _) => UpdateFreeformStatus();
        FreeformBoard.SelectionChanged += (_, _) => UpdateFreeformStatus();
        FreeformBoard.ImagePickRequested += OnFreeformImageRequested;
        // 图片只活在自由表面（§4.7）：结构化表面只把粘贴/拖放意图转过来
        CanvasBoard.ImagePasteRequested += OnCanvasImagePasteRequested;
        CanvasBoard.ImageDropRequested += OnCanvasImageDropRequested;
        CanvasBoard.ColumnsProvider = () => viewModel.Model.Columns;
        CanvasBoard.ColumnCreator = CreateCanvasColumn;
        ApplyPanelVisibility();
        ApplyMode();
        ApplySurface();
        BuildPaletteDots();

        // 面板也要材质：Prepare 装 WindowChrome 并把 Background 置空，SourceInitialized 里再调 DWM。
        GlassSurface.Prepare(this);

        Top = WorkArea.Top;
        Height = CollapsedHeight;
        Opacity = 0;
        ApplyWorkArea();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        // 诊断：面板窗口到底有没有收到这一次按下。配合 TopEdgePanelService 的
        // 「[指针] 全局按下」打点，两边对比就能区分「点击没到窗口」与「到了窗口却被画板吃掉」。
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPanelPreviewMouseDown), true);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += OnClosed;
    }

    /// <summary>画布变更回写：换成它给的实例（撤销会整份替换），再交给合并保存。</summary>
    private void PersistCanvas(CanvasDocument document)
    {
        _viewModel.Model.Canvas = document;
        _viewModel.RequestSave();
    }

    private bool IsCanvasMode => string.Equals(_viewModel.Model.PanelMode, Board.CanvasPanelMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>桌面版（布局宿主可见）时才有分隔条/栏头拖动可以命中。</summary>
    public bool IsDockMode => !IsCanvasMode;

    /// <summary>形态 A / B 只换同一块面板里的内容，不换窗口。</summary>
    private void ApplyMode()
    {
        var canvas = IsCanvasMode;
        ColumnsScroller.Visibility = canvas ? Visibility.Collapsed : Visibility.Visible;
        DockFooter.Visibility = canvas ? Visibility.Collapsed : Visibility.Visible;
        CanvasBody.Visibility = canvas ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 表面切换只改 <c>Visibility</c>：两个宿主都常驻、都指向同一份 <see cref="CanvasDocument"/>，
    /// 所以切 20 次也不会丢内容，窗口句柄与主题一个都不变。
    /// </summary>
    private void ApplySurface()
    {
        var surface = CanvasSurfaces.Normalize(_viewModel.Model.Canvas?.ActiveSurfaceId);
        var freeform = string.Equals(surface, CanvasSurfaces.Freeform, StringComparison.OrdinalIgnoreCase);

        CanvasScroller.Visibility = freeform ? Visibility.Collapsed : Visibility.Visible;
        // 这里曾经写反成 freeform ? Collapsed : Visible：结果是「结构化」时两块画板同时可见、
        // 「自由」时两块都折叠 —— 用户看到的就是「自由画板点了没反应、结构化里卡片也能被挡住」。
        FreeformScroller.Visibility = freeform ? Visibility.Visible : Visibility.Collapsed;
        StructuredTools.Visibility = freeform ? Visibility.Collapsed : Visibility.Visible;
        FreeformTools.Visibility = freeform ? Visibility.Visible : Visibility.Collapsed;
        ToolHint.Text = freeform ? "空格+拖 平移 · 滚轮缩放" : string.Empty;
        SurfaceStructuredButton.IsChecked = !freeform;
        SurfaceFreeformButton.IsChecked = freeform;
        AppLog.Info(
            $"[画布] 可见性 surface={surface} canvas={CanvasScroller.Visibility} freeform={FreeformScroller.Visibility} " +
            $"结构化工具={StructuredTools.Visibility} 自由工具={FreeformTools.Visibility}");

        if (freeform)
        {
            // B2：笔迹层是否吃鼠标只在工具改变时刷新；切面回来时强制同步一次，
            // 否则「切走再切回、工具还是笔」会命中状态过期，看起来像笔点了没反应。
            FreeformBoard.SyncToolLayer();
        }
        else
        {
            // 离开自由表面时把「空格平移」的临时状态收掉，避免回到结构化表面还顶着抓手。
            FreeformBoard.SpaceHeld = false;
        }

        UpdateToolButtons();
        UpdateCanvasStatus();
    }

    private void SetSurface(string surface)
    {
        var document = _viewModel.Model.Canvas;
        if (document is null)
        {
            return;
        }

        surface = CanvasSurfaces.Normalize(surface);
        if (string.Equals(CanvasSurfaces.Normalize(document.ActiveSurfaceId), surface, StringComparison.OrdinalIgnoreCase))
        {
            ApplySurface();
            return;
        }

        document.ActiveSurfaceId = surface;
        _viewModel.RequestSave();

        // 回来时重画一次：另一面可能刚被 undo/导入改过文档。
        if (surface == CanvasSurfaces.Freeform)
        {
            FreeformBoard.Rebuild();
        }
        else
        {
            CanvasBoard.Rebuild();
        }

        AppLog.Info($"[画布] 切到表面 {surface} 卡片={document.Cards.Count} 图元={document.Objects.Count}");
        ApplySurface();
    }

    private void OnSurfaceButtonClick(object sender, RoutedEventArgs e)
    {
        SetSurface(ReferenceEquals(sender, SurfaceFreeformButton) ? CanvasSurfaces.Freeform : CanvasSurfaces.Structured);
    }

    private void OnSurfaceAddClick(object sender, RoutedEventArgs e)
    {
        // Q2 的后半句（>4 表面才升 tab + 溢出）本期用不上：0.7.0 只有两个表面。
        // 预留行（plan §5.4）：点开出现 3 项，标「未实现」，点击只提示、不崩。
        var menu = new ContextMenu { PlacementTarget = SurfaceAddButton, Placement = PlacementMode.Bottom };
        foreach (var name in new[] { "日历", "参考文件", "图片库" })
        {
            var item = new MenuItem { Header = $"{name}（未实现）" };
            item.Click += (_, _) =>
            {
                AppLog.Info($"[画布] 预留行 {name} 未实现");
                ToastWindow.Show("还没做", $"{name} 在 0.8 之后的计划里，本期点开只是占位。");
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    /// <summary>状态行右侧 7 个色点（Q7）：点一下换色板，选中项加描边。</summary>
    private void BuildPaletteDots()
    {
        PaletteDots.Children.Clear();
        var current = _viewModel.Model.PaletteId;
        foreach (var id in PaletteIds.Selectable)
        {
            var colors = ThemePalette.For(
                id,
                _viewModel.Model.Tone,
                SystemTheme.IsLightApplicationTheme(),
                _viewModel.Model.CustomBackground,
                _viewModel.Model.CustomAccent);
            var dot = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(colors.Accent.R, colors.Accent.G, colors.Accent.B)),
                Cursor = Cursors.Hand,
                ToolTip = ThemePalette.DisplayName(id),
                BorderThickness = new Thickness(1.5),
                BorderBrush = string.Equals(id, current, StringComparison.OrdinalIgnoreCase)
                    ? (Brush)FindResource("TextPrimary")
                    : Brushes.Transparent,
            };
            var palette = id;
            dot.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                SetPalette(palette);
            };
            PaletteDots.Children.Add(dot);
        }
    }

    private void SetPalette(string palette)
    {
        if (string.Equals(_viewModel.Model.PaletteId, palette, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _viewModel.SetTheme(palette);
        BuildPaletteDots();
        UpdateCanvasStatus();
        AppLog.Info($"[主题] 状态行切换色板 → {palette}");
    }

    private void UpdateCanvasStatus()
    {
        var document = _viewModel.Model.Canvas;
        var freeform = string.Equals(CanvasSurfaces.Normalize(document?.ActiveSurfaceId), CanvasSurfaces.Freeform, StringComparison.OrdinalIgnoreCase);
        CanvasStatus.Text = freeform
            ? string.Empty
            : $"{document?.Cards.Count ?? 0} 张卡 · 撤销 {CanvasBoard.UndoDepth} 步";
        UpdateFreeformStatus();
    }

    /// <summary>自由表面的状态行：图元数 / 选中数 / 当前缩放 / 当前工具。</summary>
    private void UpdateFreeformStatus()
    {
        var document = _viewModel.Model.Canvas;
        var freeform = string.Equals(CanvasSurfaces.Normalize(document?.ActiveSurfaceId), CanvasSurfaces.Freeform, StringComparison.OrdinalIgnoreCase);
        if (!freeform)
        {
            return;
        }

        var zoom = FreeformZoom.Percent(FreeformBoard.Zoom);
        ZoomLabel.Text = $"{zoom:0}%";
        var selected = FreeformBoard.Selected.Count;
        var tool = FreeformToolCatalog.Info(_tool);
        var shortcut = tool.Shortcut is { } key ? $"（{key}）" : string.Empty;
        CanvasStatus.Text = selected > 0
            ? $"{FreeformBoard.ObjectCount} 个图元 · 选中 {selected} · 缩放 {zoom:0}% · {tool.Name}{shortcut}"
            : $"{FreeformBoard.ObjectCount} 个图元 · 缩放 {zoom:0}% · {tool.Name}{shortcut}";
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            SetTool(FreeformToolCatalog.Parse(id));
        }
    }

    /// <summary>
    /// 诊断打点：面板窗口收到的每一次按下。有这一行 → 点击到了 WPF；
    /// 没有这一行而钩子那边有「全局按下 面板内=True」→ 点击被别处的鼠标捕获截走了。
    /// </summary>
    private void OnPanelPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var local = e.GetPosition(this);
        var source = e.OriginalSource is FrameworkElement fe
            ? $"{fe.GetType().Name}{(string.IsNullOrEmpty(fe.Name) ? "" : "#" + fe.Name)}"
            : e.OriginalSource?.GetType().Name ?? "?";
        var captured = Mouse.Captured is FrameworkElement cap
            ? $"{cap.GetType().Name}{(string.IsNullOrEmpty(cap.Name) ? "" : "#" + cap.Name)}"
            : Mouse.Captured?.GetType().Name ?? "无";
        AppLog.Info($"[指针] 面板按下 local=({local.X:0},{local.Y:0}) 源={source} 捕获={captured}");
    }

    /// <summary>十工具互斥：只把点中的那个置为选中，其余一律取消。</summary>
    private void SetTool(FreeformTool tool)
    {
        _tool = tool;
        FreeformBoard.Tool = tool;
        UpdateToolButtons();
        UpdateFreeformStatus();
        AppLog.Info($"[画布] 工具 {FreeformToolCatalog.IdOf(tool)}");
    }

    private void UpdateToolButtons()
    {
        var map = new Dictionary<FreeformTool, ToggleButton>
        {
            [FreeformTool.Select] = ToolSelect,
            [FreeformTool.Pan] = ToolPan,
            [FreeformTool.Text] = ToolText,
            [FreeformTool.Image] = ToolImage,
            [FreeformTool.Rect] = ToolRect,
            [FreeformTool.Circle] = ToolCircle,
            [FreeformTool.Line] = ToolLine,
            [FreeformTool.Arrow] = ToolArrow,
            [FreeformTool.Ink] = ToolInk,
            [FreeformTool.Eraser] = ToolEraser,
            [FreeformTool.Zoom] = ToolZoom,
        };

        foreach (var (tool, button) in map)
        {
            button.IsChecked = tool == _tool;
        }
    }

    /// <summary>线型开关（C7）：只影响之后新建的图形，不改已画的。</summary>
    private void OnToolDashClick(object sender, RoutedEventArgs e)
    {
        FreeformBoard.StrokeDashed = ToolDash.IsChecked == true;
        AppLog.Info($"[画布] 线型 {(FreeformBoard.StrokeDashed ? "虚线" : "实线")}");
    }

    /// <summary>颜色与粗细（C8）：一次选一项，之后新建的笔迹/图形都用它。</summary>
    private void OnToolColorClick(object sender, RoutedEventArgs e)
    {
        if (ColorSwatches.Children.Count == 0)
        {
            BuildColorPopup();
        }

        ColorPopup.IsOpen = ToolColor.IsChecked == true;
        AppLog.Info($"[画布] 颜色面板 checked={ToolColor.IsChecked} open={ColorPopup.IsOpen}");
    }

    private void BuildColorPopup()
    {
        foreach (var (name, key) in StrokeColors)
        {
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = name,
                Tag = key,
            };
            if (key == "accent")
            {
                swatch.SetResourceReference(Border.BackgroundProperty, "Accent");
                swatch.SetResourceReference(Border.BorderBrushProperty, "BorderActive");
            }
            else
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(key));
                brush.Freeze();
                swatch.Background = brush;
                swatch.BorderBrush = brush;
            }

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                ApplyStrokeStyle(key, FreeformBoard.StrokeWidth);
                // 面板是自己关的（不是用户再点一下开关），开关状态要跟着回去，
                // 否则下一次点「色」只会把它关掉，看起来像「点了没反应」。
                ToolColor.IsChecked = false;
                ColorPopup.IsOpen = false;
            };
            ColorSwatches.Children.Add(swatch);
        }

        foreach (var width in StrokeWidths)
        {
            var button = new Button
            {
                Content = $"{width:0}",
                Padding = new Thickness(8, 2, 8, 2),
                MinWidth = 26,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = $"粗细 {width:0} DIP",
                Tag = width,
            };
            button.Click += (_, _) =>
            {
                ApplyStrokeStyle(FreeformBoard.ColorKey, width);
                ToolColor.IsChecked = false;
                ColorPopup.IsOpen = false;
            };
            WidthChoices.Children.Add(button);
        }
    }

    private static readonly (string Name, string Key)[] StrokeColors =
    {
        ("跟随色板强调色", "accent"),
        ("红", "#E5484D"),
        ("橙", "#F76B15"),
        ("黄", "#FFB224"),
        ("绿", "#30A46C"),
        ("蓝", "#0091FF"),
        ("紫", "#8E4EC6"),
        ("灰", "#8B8D98"),
    };

    private static readonly double[] StrokeWidths = { 2, 3, 5, 8 };

    private void ApplyStrokeStyle(string key, double width)
    {
        FreeformBoard.SetColorKey(key);
        FreeformBoard.SetStrokeWidth(width);
        AppLog.Info($"[画布] 颜色={key} 粗细={width:0} DIP");
    }

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        FreeformBoard.ZoomBy(FreeformZoom.Step);
        UpdateFreeformStatus();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        FreeformBoard.ZoomBy(1 / FreeformZoom.Step);
        UpdateFreeformStatus();
    }

    private void OnZoomFit(object sender, RoutedEventArgs e)
    {
        FreeformBoard.FitToView();
        UpdateFreeformStatus();
    }

    /// <summary>主屏工作区（DIP，已扣任务栏）。</summary>
    public static PanelRect WorkArea
    {
        get
        {
            Rect area = SystemParameters.WorkArea;
            return new PanelRect(area.Left, area.Top, area.Width, area.Height);
        }
    }

    public double FullHeight => PanelGeometry.ExpandedSize(WorkArea, _viewModel.Model.PanelWidthRatio, _viewModel.Model.PanelHeightRatio).Height;

    public bool IsExpanded => Height > CollapsedHeight + 2;

    public bool IsDragging => _dragging;

    /// <summary>列选择下拉是否打开：打开时点下拉项会被钩子误判成「点面板外」。</summary>
    public bool IsPickerOpen => ColumnPicker.IsDropDownOpen;

    /// <summary>
    /// 任何 Popup 开着都算：Popup 靠「鼠标捕获」判定外部点击，钩子要是这时候
    /// 把捕获清掉，颜色面板会在按下的瞬间自己关掉，色块永远收不到点击。
    /// </summary>
    public bool IsAnyPopupOpen => ColorPopup.IsOpen || ColumnPicker.IsDropDownOpen;

    /// <summary>
    /// 面板看得见、点却点不动时，最常见的原因是它被别的普通窗口压住了（面板不是置顶窗口）。
    /// 每次在面板范围内按下都把自己提回非置顶窗口的最上层，下一次点击就能落到面板上。
    /// </summary>
    public void EnsureAboveWindows()
    {
        if (!IsVisible)
        {
            return;
        }

        try
        {
            NativeMethods.RaiseToTopOfBand(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Warn($"[面板] 提升层级失败：{ex.Message}");
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 收起时是一条 760×12 的窄条，靠 DWM 把左右两端磨圆，避免像一根硬邦邦的横杠。
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DesktopLayer.TryRoundCorners(handle);

        // P2.1（D16）：面板也接材质。GlassState 由启动时的 ThemeService.Apply 决定，
        // 不可用时 TryApply 会把不透明底色写回 Background 并记 WARN（D18）。
        GlassSurface.TryApply(this);
    }

    /// <summary>停留 120 ms 后出把手（只做透明度动画，不抢焦点）。</summary>
    private void OnCanvasAddNote(object sender, RoutedEventArgs e) => AddCanvasCard("note");

    private void OnCanvasAddTodo(object sender, RoutedEventArgs e) => AddCanvasCard("todo");

    private void OnCanvasAddTimer(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.TimerSetupDialog(TimerMode.Countdown, CanvasClock.DefaultCountdownSeconds) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var center = CanvasCenter();
        CanvasBoard.AddTimer(dialog.Mode, dialog.Seconds, center.X, center.Y);
        UpdateCanvasStatus();
    }

    /// <summary>
    /// 插入图片的入口只在自由表面（B3）：结构化工具栏不再有「图片」按钮，
    /// 自由表面的「图」工具负责选文件。这段保留给自由表面的图片工具与粘贴/拖放事件复用。
    /// </summary>
    private void InsertImageAtPendingPoint()
    {
        if (!IsFreeformActive)
        {
            SetSurface(CanvasSurfaces.Freeform);
        }

        if (PickImage() is not { } file)
        {
            return;
        }

        if (FreeformBoard.AddImageObject(file, FreeformBoard.PendingImagePoint) is null)
        {
            AppLog.Warn($"画板插入图片失败：{file}");
        }

        UpdateFreeformStatus();
    }

    private string? PickImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    /// <summary>自由表面插入图片失败：宿主只负责报错与刷新状态行。</summary>
    private void OnFreeformImageRequested(object? sender, EventArgs e)
    {
        if (PickImage() is not { } file)
        {
            return;
        }

        if (FreeformBoard.AddImageObject(file, FreeformBoard.PendingImagePoint) is null)
        {
            AppLog.Warn($"自由表面插入图片失败：{file}");
        }

        UpdateFreeformStatus();
    }

    /// <summary>结构化表面 Ctrl+V 到图片：先切面，再让自由表面自己读剪贴板。</summary>
    private void OnCanvasImagePasteRequested(object? sender, EventArgs e)
    {
        if (!IsFreeformActive)
        {
            SetSurface(CanvasSurfaces.Freeform);
        }

        if (!FreeformBoard.Paste())
        {
            AppLog.Warn("画板粘贴图片失败：剪贴板内容读不出来");
        }

        UpdateFreeformStatus();
    }

    /// <summary>结构化表面被拖入图片：切面后逐个落位，位置交给自由表面算。</summary>
    private void OnCanvasImageDropRequested(object? sender, IReadOnlyList<string> files)
    {
        if (!IsFreeformActive)
        {
            SetSurface(CanvasSurfaces.Freeform);
        }

        foreach (var file in files)
        {
            if (FreeformBoard.AddImageObject(file) is null)
            {
                AppLog.Warn($"画板拖入图片失败：{file}");
            }
        }

        UpdateFreeformStatus();
    }

    private Point CanvasCenter()
    {
        return new Point(
            CanvasScroller.HorizontalOffset + Math.Max(0, CanvasScroller.ViewportWidth / 2),
            CanvasScroller.VerticalOffset + Math.Max(0, CanvasScroller.ViewportHeight / 2));
    }

    private void AddCanvasCard(string kind)
    {
        var center = CanvasCenter();
        CanvasBoard.Add(kind, center.X, center.Y);
        UpdateCanvasStatus();
    }

    public void ShowHandle()
    {
        if (!IsVisible)
        {
            Height = CollapsedHeight;
            Opacity = 0;
            BodyBorder.Visibility = Visibility.Collapsed;
            HandlePill.Visibility = Visibility.Visible;
            LayoutHost.Rebuild();
            Show();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    private void OnColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        LayoutHost.Rebuild();
        FitHost();
    }

    private void FitHost()
    {
        double width = ColumnsScroller.ViewportWidth;
        if (double.IsNaN(width) || width <= 0)
        {
            width = Math.Max(0, ActualWidth - 40);
        }

        double height = ColumnsScroller.ViewportHeight;
        if (double.IsNaN(height) || height <= 0)
        {
            height = Math.Max(0, ActualHeight - 60);
        }

        LayoutHost.FitToViewport(width, height);
    }

    public void Expand()
    {
        AppLog.Info($"[面板] expand ratioW={_viewModel.Model.PanelWidthRatio:F3} ratioH={_viewModel.Model.PanelHeightRatio:F3} full={FullHeight:F1} from={Height:F1} workAreaH={WorkArea.Height:F1}");
        ApplyMode();
        BodyBorder.Visibility = Visibility.Visible;
        HandlePill.Visibility = Visibility.Collapsed;
        AnimateHeight(FullHeight, 160, new CubicEase { EasingMode = EasingMode.EaseOut });

        // 画布要拖卡片，鼠标捕获在未激活窗口上不可靠，所以画布模式下主动激活面板。
        if (IsCanvasMode)
        {
            Activate();
            CanvasBoard.Focus();
        }
    }

    public void Collapse()
    {
        if (!IsVisible)
        {
            return;
        }

        _dragging = false;
        HandlePill.Visibility = Visibility.Visible;
        AppLog.Info($"[面板] collapse from={Height:F1}");
        AnimateHeight(CollapsedHeight, 140, new CubicEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (!IsExpanded)
            {
                Hide();
                BodyBorder.Visibility = Visibility.Collapsed;
                AppLog.Info($"[面板] hidden h={Height:F1}");
            }
        });
    }

    /// <summary>钩子给的屏幕坐标是物理像素；<see cref="PointToScreen"/> 也是物理像素，可以直接比。</summary>
    public bool ContainsScreenPoint(int x, int y)
    {
        if (!IsVisible)
        {
            return false;
        }

        try
        {
            Point topLeft = PointToScreen(new Point(0, 0));
            Point bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            return x >= topLeft.X && x <= bottomRight.X && y >= topLeft.Y && y <= bottomRight.Y;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>钩子给的屏幕坐标是物理像素；<see cref="PointFromScreen"/> 会换算成窗口内 DIP。</summary>
    public bool HitTestHandle(int screenX, int screenY)
    {
        if (!IsVisible)
        {
            return false;
        }

        try
        {
            Point local = PointFromScreen(new Point(screenX, screenY));
            return local.X >= 0 && local.X <= ActualWidth && local.Y >= 0 && local.Y <= CollapsedHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TryBeginSplitDrag(Point screenPoint) => LayoutHost.TryBeginSplitDrag(screenPoint);

    public bool IsSplitDragging => LayoutHost.IsSplitDragging;

    public void DragSplit(Point screenPoint) => LayoutHost.DragSplit(screenPoint);

    public void EndSplitDrag() => LayoutHost.EndSplitDrag();

    public bool TryBeginPaneDrag(Point screenPoint, out string paneId) => LayoutHost.TryBeginPaneDrag(screenPoint, out paneId);

    public bool IsPaneDragging => LayoutHost.IsPaneDragging;

    public void DragPane(Point screenPoint) => LayoutHost.DragPane(screenPoint);

    public void EndPaneDrag() => LayoutHost.EndPaneDrag();

    public void BeginDrag(int screenY)
    {
        _dragging = true;
        _lastScreenY = screenY;
        _dragStartHeight = Height;
        _dragStartY = screenY;
        _persistedDuringDrag = 0;
        _moveLogCount = 0;
        BeginAnimation(HeightProperty, null);
        LogHeight("begin");
    }

    public void DragTo(int screenY)
    {
        if (!_dragging)
        {
            return;
        }

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (scale <= 0)
        {
            scale = 1;
        }

        _lastScreenY = screenY;
        // 相对起始高度算位移（钩子是物理像素，先除 DPI），这样按住把手往哪拖都跟手：
        // 绝对位置（screenY - Top）会把「按下点离窗口顶边的距离」当成高度，一按就先缩到 12，
        // 手感像被弹了一下；而累计位移又会把上一次的偏差叠进去，越拖越偏。两者都不用。
        double height = ClampDragHeight(_dragStartHeight + (screenY - _dragStartY) / scale);
        Height = height;

        bool showBody = height > CollapsedHeight + 2;
        BodyBorder.Visibility = showBody ? Visibility.Visible : Visibility.Collapsed;
        HandlePill.Visibility = Visibility.Visible;

        _moveLogCount++;
        if (_moveLogCount == 1 || _moveLogCount % 10 == 0)
        {
            LogHeight($"move#{_moveLogCount}");
        }
    }

    public void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        double height = Height;

        // 松手时按「是否拉过最小展开高度」二选一：没拉过就当收回，拉过就按这个高度展开。
        // 阈值不用 FullHeight 的百分比：FullHeight 本身来自记忆比例，拖小后会把阈值一起拖小，
        // 越拖越矮，正是「拖一半就不动、还改不回来」的来源。
        if (height > MinExpandedHeight)
        {
            PersistHeight(height);
            Expand();
        }
        else
        {
            Collapse();
        }

        LogHeight("end");
    }

    private void PersistHeight(double height)
    {
        if (height <= CollapsedHeight + 2)
        {
            return;
        }

        double ratio = PanelGeometry.HeightRatio(WorkArea, height);
        if (Math.Abs(ratio - _viewModel.Model.PanelHeightRatio) > 0.001)
        {
            _viewModel.SetPanelRatios(_viewModel.Model.PanelWidthRatio, ratio);
        }

        _persistedDuringDrag++;
    }

    /// <summary>所有写 <see cref="Window.Height"/> 的地方都从这里过，禁止裸赋值。</summary>
    private double ClampCanvasHeight(double height) => PanelGeometry.ClampHeight(WorkArea, height);

    /// <summary>拖动中允许一路缩到收起高度，否则手往上抬时高度被 240 顶住、表现为「拉不动」。</summary>
    private double ClampDragHeight(double height) =>
        Math.Clamp(height, PanelGeometry.CollapsedHeight, WorkArea.Height * PanelGeometry.MaxRatio);

    /// <summary>松手后仍算「展开」的最小高度：低于它就当收回。</summary>
    private const double MinExpandedHeight = 240;

    private void LogHeight(string phase)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        AppLog.Info(
            $"[画板高度] {phase} y={_lastScreenY} workAreaH={WorkArea.Height:F1} Height={Height:F1} ActualHeight={ActualHeight:F1} " +
            $"Min={MinHeight:F1} Max={MaxHeight:F1} dpi={dpi.DpiScaleY:F2} " +
            $"persisted={_persistedDuringDrag} dragging={_dragging}");
    }

    public bool IsResizing => _resizing;

    /// <summary>钩子坐标是物理像素：先换算成窗口内 DIP，再交给纯函数判断命中哪条边。</summary>
    public bool HitTestEdge(int screenX, int screenY, out PanelEdge edge)
    {
        edge = PanelEdge.None;
        if (!IsVisible || !IsExpanded)
        {
            return false;
        }

        try
        {
            Point local = PointFromScreen(new Point(screenX, screenY));
            var window = new PanelRect(Left, Top, ActualWidth, ActualHeight);
            edge = PanelGeometry.HitTest(window, Left + local.X, Top + local.Y);
            return edge != PanelEdge.None;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void BeginResize(PanelEdge edge, int screenX, int screenY)
    {
        _resizing = true;
        _resizeEdge = edge;
        _resizeStart = new PanelRect(Left, Top, ActualWidth, ActualHeight);
        _resizeStartX = screenX;
        _resizeStartY = screenY;

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        _dpiScaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        _dpiScaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

        _dragging = false;
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
    }

    public void ResizeTo(int screenX, int screenY)
    {
        if (!_resizing)
        {
            return;
        }

        double dx = (screenX - _resizeStartX) / _dpiScaleX;
        double dy = (screenY - _resizeStartY) / _dpiScaleY;

        PanelRect next = PanelGeometry.Resize(_resizeStart, _resizeEdge, dx, dy, WorkArea);
        Left = next.Left;
        Top = next.Top;
        Width = next.Width;
        Height = ClampCanvasHeight(next.Height);

        bool showBody = Height > CollapsedHeight + 2;
        BodyBorder.Visibility = showBody ? Visibility.Visible : Visibility.Collapsed;
        HandlePill.Visibility = Visibility.Visible;
    }

    public void EndResize()
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;

        // 收到 12 DIP 的收起条上时不算一次缩放：否则会把「1% 工作区高」夹成 0.4 存下去。
        if (ActualHeight <= CollapsedHeight + 2 || ActualWidth <= 0)
        {
            ApplyWorkArea();
            return;
        }

        (double widthRatio, double heightRatio) = PanelGeometry.RatiosFromSize(
            WorkArea,
            new PanelSize(ActualWidth, ActualHeight));
        _viewModel.SetPanelRatios(widthRatio, heightRatio);
    }

    /// <summary>工作区变化（任务栏挪动、分辨率/缩放变化）后重算尺寸；收起状态保持 12 DIP。</summary>
    public void ApplyWorkArea()
    {
        PanelSize size = PanelGeometry.ExpandedSize(
            WorkArea,
            _viewModel.Model.PanelWidthRatio,
            _viewModel.Model.PanelHeightRatio);
        PanelRect placed = PanelGeometry.PlaceTopCenter(WorkArea, size);

        Width = placed.Width;
        Left = placed.Left;
        Top = placed.Top;

        if (IsExpanded)
        {
            Height = ClampCanvasHeight(size.Height);
        }
    }

    /// <summary>面板只看 PanelVisibleColumnIds 里的栏；空列表 = 跟桌面一样全显示。</summary>
    private void ApplyPanelVisibility()
    {
        var configured = _viewModel.Model.PanelVisibleColumnIds;
        LayoutHost.SetVisibleColumns(configured.Count == 0 ? null : _viewModel.Model.VisibleColumnIds(panel: true));
        LayoutHost.Rebuild();
        FitHost();
    }

    private string? CreateCanvasColumn(string? suggested)
    {
        var dialog = new ColumnNameDialog(string.IsNullOrWhiteSpace(suggested) ? ColumnNameDialog.DefaultName : suggested)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        string? id = _viewModel.AddColumnAndGetId(dialog.ColumnTitle);
        CanvasBoard.ColumnCreatorTitle = string.IsNullOrWhiteSpace(dialog.ColumnTitle)
            ? ColumnNameDialog.DefaultName
            : dialog.ColumnTitle;
        return id;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SystemParameters.WorkArea), StringComparison.Ordinal))
        {
            ApplyWorkArea();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        ApplyWorkArea();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _viewModel.Columns.CollectionChanged -= OnColumnsChanged;
        ColumnsScroller.SizeChanged -= OnSizeChangedFit;
    }

    private void OnSizeChangedFit(object sender, SizeChangedEventArgs e)
    {
        FitHost();
    }

    /// <summary>自由表面当前是否可见（工具快捷键、空格平移只在这一面生效）。</summary>
    private bool IsFreeformActive =>
        IsCanvasMode && string.Equals(
            CanvasSurfaces.Normalize(_viewModel.Model.Canvas?.ActiveSurfaceId),
            CanvasSurfaces.Freeform,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsTextEditing => Keyboard.FocusedElement is TextBox;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // 自由表面有选中时，Esc 先清选择；再按一次才收起面板。
            if (IsFreeformActive && FreeformBoard.Selected.Count > 0)
            {
                FreeformBoard.ClearSelection();
                UpdateFreeformStatus();
                e.Handled = true;
                return;
            }

            Collapse();
            e.Handled = true;
            return;
        }

        // Ctrl+1 / Ctrl+2 切表面（plan §5.4 快捷键表）
        if (IsCanvasMode && Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.D1 || e.Key == Key.D2))
        {
            SetSurface(e.Key == Key.D2 ? CanvasSurfaces.Freeform : CanvasSurfaces.Structured);
            e.Handled = true;
            return;
        }

        // 空格临时切手形（松手复原），输入框里不抢空格
        if (IsFreeformActive && e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && !IsTextEditing)
        {
            FreeformBoard.SpaceHeld = true;
            e.Handled = true;
            return;
        }

        // V/H/T/R/A/B/E 选工具（单一来源：FreeformToolCatalog）
        if (IsFreeformActive && Keyboard.Modifiers == ModifierKeys.None && !IsTextEditing &&
            e.Key >= Key.A && e.Key <= Key.Z &&
            FreeformToolCatalog.FromShortcut((char)('A' + (e.Key - Key.A))) is { } shortcutTool)
        {
            SetTool(shortcutTool);
            e.Handled = true;
            return;
        }

        if (IsCanvasMode && e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (IsFreeformActive)
            {
                FreeformBoard.Undo();
                UpdateFreeformStatus();
            }
            else
            {
                CanvasBoard.Undo();
                UpdateCanvasStatus();
            }

            e.Handled = true;
            return;
        }

        if (IsCanvasMode && e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (IsFreeformActive)
            {
                FreeformBoard.Redo();
                UpdateFreeformStatus();
            }
            else
            {
                CanvasBoard.Redo();
                UpdateCanvasStatus();
            }

            e.Handled = true;
            return;
        }

        if (IsCanvasMode && e.Key == Key.Delete)
        {
            if (IsFreeformActive)
            {
                FreeformBoard.DeleteSelected();
                UpdateFreeformStatus();
            }
            else
            {
                CanvasBoard.DeleteSelected();
                UpdateCanvasStatus();
            }

            e.Handled = true;
            return;
        }

        // F2 改名：结构化卡片走 CanvasBoard，自由表面暂无「改名」概念（plan §5.5）
        if (IsCanvasMode && e.Key == Key.F2 && !IsFreeformActive && !IsTextEditing)
        {
            CanvasBoard.BeginRenameSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            QuickBox.Clear();
            QuickBox.Focus();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && FreeformBoard.SpaceHeld)
        {
            FreeformBoard.SpaceHeld = false;
            e.Handled = true;
        }
    }

    private void OnQuickAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ColumnPicker.SelectedItem is not ColumnViewModel column)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(QuickBox.Text))
        {
            return;
        }

        column.DraftText = QuickBox.Text;
        column.AddDraft();
        QuickBox.Clear();
        e.Handled = true;
    }

    private void AnimateHeight(double target, int milliseconds, IEasingFunction easing, Action? completed = null)
    {
        BeginAnimation(HeightProperty, null);
        // 收起的动画目标就是 12 DIP，不能套「展开下限 240」那套夹取，否则永远收不到底、
        // 窗口也就永远不会 Hide——表现成「点外部后收回到一半停在那里」。
        target = ClampDragHeight(target);
        var animation = new DoubleAnimation(Height, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop,
        };

        animation.Completed += (_, _) =>
        {
            BeginAnimation(HeightProperty, null);
            Height = target;
            completed?.Invoke();
        };

        BeginAnimation(HeightProperty, animation);
    }
}
