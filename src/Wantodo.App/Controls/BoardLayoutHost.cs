using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wantodo.App.ViewModels;
using Wantodo.Core;

namespace Wantodo.App.Controls;

/// <summary>
/// 按 <see cref="Board.Layout"/> 渲染停靠布局（叶 = 一栏，Split = 两路 + 6px 分隔条）。
/// 主窗口与顶边面板共用这一份渲染与拖分隔条的逻辑；栏本身长什么样由调用方给的
/// <see cref="PaneTemplate"/> 决定（主窗口是完整栏，面板是紧凑栏）。
/// 只在「结构变了」时调 <see cref="Rebuild"/>（启动、加栏、删栏、停靠、DPI 变化），拖分隔条不走重建。
/// </summary>
public sealed class BoardLayoutHost : Grid
{
    private readonly Dictionary<GridSplitter, SplitView> _splitViews = new();
    private readonly Dictionary<string, FrameworkElement> _paneElements = new(StringComparer.Ordinal);
    private Board? _board;
    private IReadOnlyList<ColumnViewModel>? _columns;
    private HashSet<string>? _visibleIds;
    private SplitView? _activeSplit;
    private Point _activeSplitStart;
    private double _activeSplitFirst;
    private double _activeSplitSecond;
    private string? _dragPaneId;
    private string? _dragTargetId;

    /// <summary>叶子的内容模板；为 null 时退回资源里的 <c>PaneTemplate</c>。</summary>
    public DataTemplate? PaneTemplate { get; set; }

    /// <summary>拖完分隔条、比例已写回模型时触发；调用方负责落盘。</summary>
    public event EventHandler? SaveRequested;

    /// <summary>接管数据源：主窗口与面板各调一次。</summary>
    public void Attach(Board board, IReadOnlyList<ColumnViewModel> columns)
    {
        _board = board;
        _columns = columns;
    }

    /// <summary>
    /// 某个界面只看一部分栏（Phase 24 B2 的 per-surface visibility）。
    /// 传 null 表示跟随整块板的布局；传集合时按该顺序临时排成均衡布局，不动 data.json 里的树。
    /// </summary>
    public void SetVisibleColumns(HashSet<string>? ids)
    {
        _visibleIds = ids;
    }

    private LayoutNode? ActiveLayout =>
        _board is null ? null : _visibleIds is null ? _board.Layout : LayoutTree.Balanced(_visibleIds.ToList());

    /// <summary>布局树要求的最小尺寸（栏数多、窗口窄时用它撑出滚动条）。</summary>
    public Size MinSize
    {
        get
        {
            LayoutNode? layout = ActiveLayout;
            if (layout is null)
            {
                return Size.Empty;
            }

            (double width, double height) = LayoutTree.ComputeMinSize(layout);
            return new Size(width, height);
        }
    }

    /// <summary>重建可视树。布局为空时清空并记一条 WARN，不渲染任何栏。</summary>
    public void Rebuild()
    {
        _splitViews.Clear();
        _paneElements.Clear();
        _activeSplit = null;
        _dragPaneId = null;
        _dragTargetId = null;
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();

        LayoutNode? layout = ActiveLayout;
        if (layout is null || _columns is null)
        {
            AppLog.Warn("布局为空，界面不渲染任何栏");
            return;
        }

        AppLog.Info($"绘制布局：根={layout.Type}/{layout.Orientation} 叶={LayoutTree.LeafIds(layout).Count}");

        if (BuildLayoutElement(layout, string.Empty) is { } element)
        {
            Children.Add(element);
        }
    }

    /// <summary>
    /// 宿主尺寸 = max(视口, 布局最小尺寸)：窗口小到放不下时让内容大于视口，
    /// 走 ScrollViewer 滚动，而不是把每一栏压成一条缝。
    /// </summary>
    public void FitToViewport(double viewportWidth, double viewportHeight)
    {
        LayoutNode? layout = ActiveLayout;
        if (layout is null)
        {
            return;
        }

        (double minWidth, double minHeight) = LayoutTree.ComputeMinSize(layout);
        Width = Math.Max(viewportWidth, minWidth);
        Height = Math.Max(viewportHeight, minHeight);
    }

    private FrameworkElement? BuildLayoutElement(LayoutNode node, string path)
    {
        if (node.IsLeaf)
        {
            ColumnViewModel? column = _columns?.FirstOrDefault(
                c => string.Equals(c.Id, node.PaneId, StringComparison.Ordinal));

            if (column is null)
            {
                return null;
            }

            DataTemplate? template = PaneTemplate ?? TryFindResource("PaneTemplate") as DataTemplate;

            var element = new ContentControl
            {
                Content = column,
                ContentTemplate = template,
                Focusable = false,
            };

            _paneElements[node.PaneId ?? string.Empty] = element;

            // 形式层的硬阴影：垫一块同形状的色块，向右下偏 4 DIP。flat 时这是透明刷，看不见。
            var shadow = new Border
            {
                Margin = new Thickness(4),
                IsHitTestVisible = false,
                RenderTransform = new System.Windows.Media.TranslateTransform(4, 4),
            };
            shadow.SetResourceReference(Border.BackgroundProperty, "ThemeShadowBrush");
            shadow.SetResourceReference(Border.CornerRadiusProperty, "ThemeCornerRadius");

            var stack = new Grid();
            stack.Children.Add(shadow);
            stack.Children.Add(element);
            return stack;
        }

        if (node.First is null || node.Second is null)
        {
            return null;
        }

        bool horizontal = node.IsHorizontal;
        double ratio = Math.Clamp(node.Ratio, LayoutNode.MinRatio, LayoutNode.MaxRatio);
        (double firstMinWidth, double firstMinHeight) = LayoutTree.ComputeMinSize(node.First);
        (double secondMinWidth, double secondMinHeight) = LayoutTree.ComputeMinSize(node.Second);

        var grid = new Grid();

        ColumnDefinition? firstColumn = null;
        ColumnDefinition? secondColumn = null;
        RowDefinition? firstRow = null;
        RowDefinition? secondRow = null;

        if (horizontal)
        {
            firstColumn = new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star), MinWidth = firstMinWidth };
            secondColumn = new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star), MinWidth = secondMinWidth };
            grid.ColumnDefinitions.Add(firstColumn);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LayoutTree.SplitterSize) });
            grid.ColumnDefinitions.Add(secondColumn);
        }
        else
        {
            firstRow = new RowDefinition { Height = new GridLength(ratio, GridUnitType.Star), MinHeight = firstMinHeight };
            secondRow = new RowDefinition { Height = new GridLength(1 - ratio, GridUnitType.Star), MinHeight = secondMinHeight };
            grid.RowDefinitions.Add(firstRow);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LayoutTree.SplitterSize) });
            grid.RowDefinitions.Add(secondRow);
        }

        FrameworkElement? first = BuildLayoutElement(node.First, path + "0");
        FrameworkElement? second = BuildLayoutElement(node.Second, path + "1");
        if (first is null || second is null)
        {
            return grid;
        }

        Brush divider = TryFindResource("Divider") as Brush ?? Brushes.Transparent;
        var splitter = new GridSplitter
        {
            ShowsPreview = false,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            // 平时给一条淡分隔线：面板没有窗口边框，纯透明分隔条看不出「这里有缝」。
            Background = divider,
            Focusable = false,
            Cursor = horizontal ? System.Windows.Input.Cursors.SizeWE : System.Windows.Input.Cursors.SizeNS,
            // 显式给尺寸：GridSplitter 默认模板按内容量，空内容会量出 0×0（UIA 里是 ∞，点不到）。
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = horizontal ? LayoutTree.SplitterSize : double.NaN,
            Height = horizontal ? double.NaN : LayoutTree.SplitterSize,
        };

        Brush accent = TryFindResource("Accent") as Brush ?? Brushes.Transparent;
        splitter.MouseEnter += (_, _) => splitter.Background = accent;
        splitter.MouseLeave += (_, _) =>
        {
            if (!ReferenceEquals(_activeSplit?.Splitter, splitter))
            {
                splitter.Background = divider;
            }
        };

        if (horizontal)
        {
            SetColumn(first, 0);
            SetColumn(splitter, 1);
            SetColumn(second, 2);
        }
        else
        {
            SetRow(first, 0);
            SetRow(splitter, 1);
            SetRow(second, 2);
        }

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);

        var view = new SplitView(node, path, horizontal, firstColumn, secondColumn, firstRow, secondRow, splitter, divider);
        _splitViews[splitter] = view;
        splitter.DragCompleted += (_, _) => OnSplitterDragCompleted(view);

        return grid;
    }

    /// <summary>
    /// 拖完分隔条：按实际尺寸回算比例写进模型，再把两路设回星号。
    /// 这一步不改窗口尺寸（Phase 9 的硬要求：缩放窗口时整体保持原样）。
    /// </summary>
    private void OnSplitterDragCompleted(SplitView view)
    {
        double first;
        double second;

        if (view.Horizontal)
        {
            first = view.FirstColumn?.ActualWidth ?? 0;
            second = view.SecondColumn?.ActualWidth ?? 0;
        }
        else
        {
            first = view.FirstRow?.ActualHeight ?? 0;
            second = view.SecondRow?.ActualHeight ?? 0;
        }

        double total = first + second;
        if (total <= 0 || _board?.Layout is null || _visibleIds is not null)
        {
            return;
        }

        if (!LayoutTree.SetRatio(_board.Layout, view.Path, first / total))
        {
            return;
        }

        ApplySplitLengths(view.Node, view);
        SaveRequested?.Invoke(this, EventArgs.Empty);
        AppLog.Info($"分隔条 {view.Path}：比例 {view.Node.Ratio:F2}");
    }

    /// <summary>
    /// 面板里没有可激活的窗口，WPF 的鼠标捕获不可靠（Phase 17 的把手已经踩过一次），
    /// 所以分隔条也改由全局钩子驱动：按下时找分隔条，移动时直接改两列/两行的像素宽，松手回算比例。
    /// </summary>
    public bool TryBeginSplitDrag(Point screenPoint)
    {
        if (_board?.Layout is null)
        {
            return false;
        }

        if (!TryToLocal(screenPoint, out Point local))
        {
            return false;
        }

        GridSplitter? splitter = HitAncestor<GridSplitter>(local);
        if (splitter is null || !_splitViews.TryGetValue(splitter, out SplitView? view))
        {
            return false;
        }

        _activeSplit = view;
        _activeSplitStart = screenPoint;
        _activeSplitFirst = view.Horizontal
            ? view.FirstColumn?.ActualWidth ?? 0
            : view.FirstRow?.ActualHeight ?? 0;
        _activeSplitSecond = view.Horizontal
            ? view.SecondColumn?.ActualWidth ?? 0
            : view.SecondRow?.ActualHeight ?? 0;
        splitter.Background = TryFindResource("Accent") as Brush ?? Brushes.Transparent;
        return true;
    }

    public bool IsSplitDragging => _activeSplit is not null;

    public void DragSplit(Point screenPoint)
    {
        if (_activeSplit is null)
        {
            return;
        }

        SplitView view = _activeSplit;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double delta = view.Horizontal
            ? (screenPoint.X - _activeSplitStart.X) / scale
            : (screenPoint.Y - _activeSplitStart.Y) / scale;

        double firstMin = view.Horizontal
            ? view.FirstColumn?.MinWidth ?? 0
            : view.FirstRow?.MinHeight ?? 0;
        double secondMin = view.Horizontal
            ? view.SecondColumn?.MinWidth ?? 0
            : view.SecondRow?.MinHeight ?? 0;

        double first = Math.Clamp(_activeSplitFirst + delta, firstMin, _activeSplitFirst + _activeSplitSecond - secondMin);
        double second = _activeSplitFirst + _activeSplitSecond - first;

        if (view.Horizontal)
        {
            if (view.FirstColumn is not null)
            {
                view.FirstColumn.Width = new GridLength(first, GridUnitType.Pixel);
            }

            if (view.SecondColumn is not null)
            {
                view.SecondColumn.Width = new GridLength(second, GridUnitType.Pixel);
            }
        }
        else
        {
            if (view.FirstRow is not null)
            {
                view.FirstRow.Height = new GridLength(first, GridUnitType.Pixel);
            }

            if (view.SecondRow is not null)
            {
                view.SecondRow.Height = new GridLength(second, GridUnitType.Pixel);
            }
        }
    }

    public void EndSplitDrag()
    {
        if (_activeSplit is null)
        {
            return;
        }

        SplitView view = _activeSplit;
        _activeSplit = null;
        OnSplitterDragCompleted(view);
        view.Splitter.Background = view.Divider;
    }

    public bool TryBeginPaneDrag(Point screenPoint, out string paneId)
    {
        paneId = string.Empty;
        if (_paneElements.Count == 0)
        {
            return false;
        }

        if (!TryToLocal(screenPoint, out Point local))
        {
            return false;
        }

        ContentControl? pane = HitAncestor<ContentControl>(local);
        if (pane is null)
        {
            return false;
        }

        string? id = _paneElements.FirstOrDefault(kv => ReferenceEquals(kv.Value, pane)).Key;
        if (id is null)
        {
            return false;
        }

        // 栏头 26 DIP 才是拖动区，栏内文字仍归按钮点击。
        Point inPane = Mouse.GetPosition(pane);
        if (inPane.Y > 26)
        {
            return false;
        }

        _dragPaneId = id;
        pane.Opacity = 0.5;
        paneId = id;
        return true;
    }

    public bool IsPaneDragging => _dragPaneId is not null;

    public void DragPane(Point screenPoint)
    {
        if (_dragPaneId is null)
        {
            return;
        }

        string? target = FindPaneAt(screenPoint);
        if (string.Equals(target, _dragTargetId, StringComparison.Ordinal))
        {
            return;
        }

        if (_dragTargetId is not null && _paneElements.TryGetValue(_dragTargetId, out FrameworkElement? old))
        {
            old.Opacity = 1;
        }

        _dragTargetId = target is not null && !string.Equals(target, _dragPaneId, StringComparison.Ordinal)
            ? target
            : null;

        if (_dragTargetId is not null && _paneElements.TryGetValue(_dragTargetId, out FrameworkElement? current))
        {
            current.Opacity = 0.7;
        }
    }

    public void EndPaneDrag()
    {
        string? source = _dragPaneId;
        string? target = _dragTargetId;
        _dragPaneId = null;
        _dragTargetId = null;

        foreach (FrameworkElement element in _paneElements.Values)
        {
            element.Opacity = 1;
        }

        if (source is null || target is null || _board?.Layout is null || _visibleIds is not null)
        {
            return;
        }

        _board.Layout = LayoutTree.SwapPanes(_board.Layout, source, target);
        AppLog.Info($"面板拖栏：{source} ↔ {target}");
        SaveRequested?.Invoke(this, EventArgs.Empty);
        Rebuild();
    }

    private string? FindPaneAt(Point screenPoint)
    {
        if (!TryToLocal(screenPoint, out Point local))
        {
            return null;
        }

        ContentControl? pane = HitAncestor<ContentControl>(local);
        if (pane is null)
        {
            return null;
        }

        return _paneElements.FirstOrDefault(kv => ReferenceEquals(kv.Value, pane)).Key;
    }

    /// <summary>
    /// 屏坐标换算成控件内坐标。画布模式下这块布局被 Collapsed、
    /// 甚至可能还没挂到 PresentationSource，直接调 PointFromScreen 会抛 InvalidOperationException。
    /// </summary>
    private bool TryToLocal(Point screenPoint, out Point local)
    {
        local = default;
        if (PresentationSource.FromVisual(this) is null)
        {
            return false;
        }

        try
        {
            local = PointFromScreen(screenPoint);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private T? HitAncestor<T>(Point local)
        where T : DependencyObject
    {
        HitTestResult? result = VisualTreeHelper.HitTest(this, local);
        DependencyObject? current = result?.VisualHit;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void ApplySplitLengths(LayoutNode node, SplitView view)
    {
        double ratio = Math.Clamp(node.Ratio, LayoutNode.MinRatio, LayoutNode.MaxRatio);

        if (view.Horizontal)
        {
            if (view.FirstColumn is not null)
            {
                view.FirstColumn.Width = new GridLength(ratio, GridUnitType.Star);
            }

            if (view.SecondColumn is not null)
            {
                view.SecondColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
        else
        {
            if (view.FirstRow is not null)
            {
                view.FirstRow.Height = new GridLength(ratio, GridUnitType.Star);
            }

            if (view.SecondRow is not null)
            {
                view.SecondRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
    }

    /// <summary>一次 Split 的可视化句柄，拖完分隔条要用它把尺寸设回星号。</summary>
    private sealed record SplitView(
        LayoutNode Node,
        string Path,
        bool Horizontal,
        ColumnDefinition? FirstColumn,
        ColumnDefinition? SecondColumn,
        RowDefinition? FirstRow,
        RowDefinition? SecondRow,
        GridSplitter Splitter,
        Brush Divider);
}
