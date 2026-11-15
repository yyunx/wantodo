using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        var canvasStore = new CanvasStore(DataRootService.Paths.CanvasFile);
        CanvasBoard.Attach(canvasStore.Load(), canvasStore);
        CanvasBoard.StatusChanged += (_, _) => UpdateCanvasStatus();
        ApplyMode();
        UpdateCanvasStatus();

        Top = WorkArea.Top;
        Height = CollapsedHeight;
        Opacity = 0;
        ApplyWorkArea();

        PreviewKeyDown += OnPreviewKeyDown;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += OnClosed;
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

    private void UpdateCanvasStatus() => CanvasStatus.Text = $"{CanvasBoard.Document.Items.Count} 张 · Ctrl+Z 撤销";

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 收起时是一条 760×12 的窄条，靠 DWM 把左右两端磨圆，避免像一根硬邦邦的横杠。
        DesktopLayer.TryRoundCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    /// <summary>停留 120 ms 后出把手（只做透明度动画，不抢焦点）。</summary>
    private void OnCanvasAddNote(object sender, RoutedEventArgs e) => AddCanvasCard("note");

    private void OnCanvasAddTodo(object sender, RoutedEventArgs e) => AddCanvasCard("todo");

    private void OnCanvasAddTimer(object sender, RoutedEventArgs e) => AddCanvasCard("timer");

    private void OnCanvasAddLink(object sender, RoutedEventArgs e) => AddCanvasCard("link");

    private void OnCanvasAddImage(object sender, RoutedEventArgs e) => AddCanvasCard("image");

    private void AddCanvasCard(string kind)
    {
        var centerX = CanvasScroller.HorizontalOffset + Math.Max(0, CanvasScroller.ViewportWidth / 2);
        var centerY = CanvasScroller.VerticalOffset + Math.Max(0, CanvasScroller.ViewportHeight / 2);
        CanvasBoard.Add(kind, centerX, centerY);
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
        AnimateHeight(CollapsedHeight, 140, new CubicEase { EasingMode = EasingMode.EaseIn }, () =>
        {
            if (!IsExpanded)
            {
                Hide();
                BodyBorder.Visibility = Visibility.Collapsed;
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
        _dragStartHeight = Height;
        _dragStartY = screenY;
        BeginAnimation(HeightProperty, null);
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

        double height = Math.Clamp(_dragStartHeight + (screenY - _dragStartY) / scale, CollapsedHeight, FullHeight);
        Height = height;

        bool showBody = height > CollapsedHeight + 2;
        BodyBorder.Visibility = showBody ? Visibility.Visible : Visibility.Collapsed;
        HandlePill.Visibility = Visibility.Visible;
    }

    public void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (Height >= FullHeight * 0.4)
        {
            Expand();
        }
        else
        {
            Collapse();
        }
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
        Height = next.Height;

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
            Height = size.Height;
        }
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Collapse();
            e.Handled = true;
            return;
        }

        if (IsCanvasMode && e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CanvasBoard.Undo();
            UpdateCanvasStatus();
            e.Handled = true;
            return;
        }

        if (IsCanvasMode && e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CanvasBoard.Redo();
            UpdateCanvasStatus();
            e.Handled = true;
            return;
        }

        if (IsCanvasMode && e.Key == Key.Delete)
        {
            CanvasBoard.DeleteSelected();
            UpdateCanvasStatus();
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
