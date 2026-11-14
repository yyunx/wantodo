using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    private readonly Dictionary<LayoutNode, SplitView> _splitViews = new();
    private Board? _board;
    private IReadOnlyList<ColumnViewModel>? _columns;

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

    /// <summary>布局树要求的最小尺寸（栏数多、窗口窄时用它撑出滚动条）。</summary>
    public Size MinSize
    {
        get
        {
            if (_board?.Layout is null)
            {
                return Size.Empty;
            }

            (double width, double height) = LayoutTree.ComputeMinSize(_board.Layout);
            return new Size(width, height);
        }
    }

    /// <summary>重建可视树。布局为空时清空并记一条 WARN，不渲染任何栏。</summary>
    public void Rebuild()
    {
        _splitViews.Clear();
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();

        if (_board?.Layout is null || _columns is null)
        {
            AppLog.Warn("布局为空，界面不渲染任何栏");
            return;
        }

        AppLog.Info($"绘制布局：根={_board.Layout.Type}/{_board.Layout.Orientation} 叶={LayoutTree.LeafIds(_board.Layout).Count}");

        if (BuildLayoutElement(_board.Layout, string.Empty) is { } element)
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
        if (_board?.Layout is null)
        {
            return;
        }

        (double minWidth, double minHeight) = LayoutTree.ComputeMinSize(_board.Layout);
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

            return new ContentControl
            {
                Content = column,
                ContentTemplate = template,
                Focusable = false,
            };
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

        var splitter = new GridSplitter
        {
            ShowsPreview = false,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            Background = Brushes.Transparent,
            Focusable = false,
            Cursor = horizontal ? System.Windows.Input.Cursors.SizeWE : System.Windows.Input.Cursors.SizeNS,
            // 显式给尺寸：GridSplitter 默认模板按内容量，空内容会量出 0×0（UIA 里是 ∞，点不到）。
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = horizontal ? LayoutTree.SplitterSize : double.NaN,
            Height = horizontal ? double.NaN : LayoutTree.SplitterSize,
        };

        var accent = TryFindResource("Accent") as Brush ?? Brushes.Transparent;
        splitter.MouseEnter += (_, _) => splitter.Background = accent;
        splitter.MouseLeave += (_, _) => splitter.Background = Brushes.Transparent;

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

        var view = new SplitView(path, horizontal, firstColumn, secondColumn, firstRow, secondRow);
        _splitViews[node] = view;
        splitter.DragCompleted += (_, _) => OnSplitterDragCompleted(node, view);

        return grid;
    }

    /// <summary>
    /// 拖完分隔条：按实际尺寸回算比例写进模型，再把两路设回星号。
    /// 这一步不改窗口尺寸（Phase 9 的硬要求：缩放窗口时整体保持原样）。
    /// </summary>
    private void OnSplitterDragCompleted(LayoutNode node, SplitView view)
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
        if (total <= 0 || _board?.Layout is null)
        {
            return;
        }

        if (!LayoutTree.SetRatio(_board.Layout, view.Path, first / total))
        {
            return;
        }

        ApplySplitLengths(node, view);
        SaveRequested?.Invoke(this, EventArgs.Empty);
        AppLog.Info($"分隔条 {view.Path}：比例 {node.Ratio:F2}");
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
        string Path,
        bool Horizontal,
        ColumnDefinition? FirstColumn,
        ColumnDefinition? SecondColumn,
        RowDefinition? FirstRow,
        RowDefinition? SecondRow);
}
