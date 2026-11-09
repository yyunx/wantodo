using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wantodo.App.Interop;
using Wantodo.App.ViewModels;
using Wantodo.Core;

namespace Wantodo.App;

public partial class MainWindow : Window
{
    private const string NoteDataFormat = "wantodo/note";

    /// <summary>一栏能压到的最窄宽度（DIP）。再窄就横向滚动，不让文字被剪成一条缝。</summary>
    private const double MinColumnWidth = 190;

    /// <summary>栏与栏之间的间距：模板里是 Margin="4,0"，左右各 4。</summary>
    private const double ColumnGap = 8;

    /// <summary>加栏时希望保住的舒适宽度；窗口放不下就先撑窗口，撑不动才退到最小宽度。</summary>
    private const double PreferredColumnWidth = 280;

    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(MinColumnWidth));

    private readonly BoardViewModel _viewModel;

    private IntPtr _hwnd;
    private ShellRestartWatcher? _shellRestartWatcher;

    private Point _dragStart;
    private NoteViewModel? _draggingNote;

    public MainWindow(Board board, JsonBoardStore store)
    {
        // 数据里一栏都没有时界面会是空的，给个最小可用的占位栏（不改用户的 JSON，只在内存里补）
        if (board.Columns.Count == 0)
        {
            board.Columns.Add(new NoteColumn { Title = "未命名栏" });
            AppLog.Warn("数据里没有栏，已临时补一栏");
        }

        InitializeComponent();

        _viewModel = new BoardViewModel(board, store);
        DataContext = _viewModel;

        _viewModel.Columns.CollectionChanged += OnColumnsChanged;
        Loaded += (_, _) => UpdateColumnWidths();
        SizeChanged += (_, _) => UpdateColumnWidths();

        ApplyPlacement();
    }

    /// <summary>每栏宽度，由窗口可用宽度和栏数算出来；窄到放不下就保持最小宽度并横向滚动。</summary>
    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    private Board Board => _viewModel.Model;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        AppLog.Info($"窗口就绪 hwnd=0x{_hwnd.ToInt64():X} {Width}×{Height} DIP @ {Left},{Top}");

        DesktopLayer.TryRoundCorners(_hwnd);

        if (!DesktopLayer.Attach(_hwnd, Board.Surface))
        {
            AppLog.Warn($"没能挂到 {Board.Surface} 层；" + DesktopLayer.Describe(_hwnd));
        }

        if (string.Equals(Board.Surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase))
        {
            _shellRestartWatcher = ShellRestartWatcher.Install(_hwnd, OnShellRestarted);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _shellRestartWatcher?.Dispose();
        _shellRestartWatcher = null;

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        _viewModel.SavePlacement(Left, Top, width, height);
        AppLog.Info($"已保存布局 {width}×{height} @ {Left},{Top}");

        base.OnClosing(e);
    }

    private void OnShellRestarted()
    {
        var ok = DesktopLayer.Attach(_hwnd, Board.Surface);
        AppLog.Info($"Explorer 重启后重挂 {(ok ? "成功" : "失败")}；" + DesktopLayer.Describe(_hwnd));
    }

    private void ApplyPlacement()
    {
        var placement = Board.Placement;
        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);

        if (placement.HasPosition)
        {
            Left = placement.Left!.Value;
            Top = placement.Top!.Value;
        }
        else
        {
            // 首次启动放到主屏右上角，像手机小组件的习惯位置
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 48;
            Top = area.Top + 48;
            AppLog.Info($"首次启动，默认落点 {Left},{Top}");
        }

        ClampIntoVirtualScreen();
    }

    /// <summary>显示器被拔掉 / 分辨率变了以后，别让窗口留在屏幕外找不回来。</summary>
    private void ClampIntoVirtualScreen()
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        var clampedLeft = Math.Clamp(Left, virtualLeft, Math.Max(virtualLeft, virtualRight - 80));
        var clampedTop = Math.Clamp(Top, virtualTop, Math.Max(virtualTop, virtualBottom - 40));

        if (Math.Abs(clampedLeft - Left) > 0.5 || Math.Abs(clampedTop - Top) > 0.5)
        {
            AppLog.Warn($"保存的位置 {Left},{Top} 在屏幕外，已拉回到 {clampedLeft},{clampedTop}");
            Left = clampedLeft;
            Top = clampedTop;
        }
    }

    // ---------- 顶栏 ----------

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标已经松开时 DragMove 会抛；拖拽而已，忽略
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ---------- 栏 ----------

    /// <summary>记住「现在在哪一栏操作」，新栏就插在它右边（见 decisions.md D9）。</summary>
    private void OnColumnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColumnViewModel column })
        {
            _viewModel.SetActiveColumn(column);
        }
    }

    /// <summary>点栏里任意位置也算「在这一栏操作」，比只认键盘焦点稳。</summary>
    private void OnColumnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColumnViewModel column })
        {
            _viewModel.SetActiveColumn(column);
        }
    }

    /// <summary>
    /// 栏宽 = 可用宽度平均分；分到比 <see cref="MinColumnWidth"/> 还窄就停在最小宽度，
    /// 让外层横向滚动顶出去，而不是把每栏压成一条缝。
    /// </summary>
    private void UpdateColumnWidths()
    {
        int count = _viewModel.Columns.Count;
        if (count <= 0)
        {
            return;
        }

        double available = ColumnsScroller.ActualWidth;
        if (available <= 0)
        {
            available = ActualWidth - 16;
        }

        double width = (available - ColumnGap * count) / count;
        ColumnWidth = Math.Max(MinColumnWidth, width);
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            GrowWindowForColumns();
        }

        UpdateColumnWidths();
    }

    /// <summary>
    /// 加栏时如果按最小宽度已经放不下，先把窗口往右撑（撑到屏幕边缘为止），
    /// 实在放不下才交给横向滚动。窗口本来就更宽时不动它。
    /// </summary>
    private void GrowWindowForColumns()
    {
        int count = _viewModel.Columns.Count;
        // 目标是一栏 PreferredColumnWidth 宽；窗口不够就撑窗口，撑到屏幕边界为止
        double preferred = PreferredColumnWidth;
        double needed = ColumnGap * count + preferred * count + ColumnGap;
        double current = ActualWidth > 0 ? ActualWidth : Width;

        if (current >= needed)
        {
            return;
        }

        var work = SystemParameters.WorkArea;
        double width = Math.Min(needed, work.Width - ColumnGap);
        double left = Left;

        if (left + width > work.Right)
        {
            left = Math.Max(work.Left, work.Right - width);
        }

        AppLog.Info($"加栏后窗口加宽 {current:F0} → {width:F0} DIP @ {left:F0}");
        Left = left;
        Width = width;
    }

    private void OnColumnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: ColumnViewModel column })
        {
            return;
        }

        column.BeginTitleEdit();
        e.Handled = true;
    }

    private void OnTitleEditBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 折叠状态下也会触发 Loaded，只有真正显示出来才抢焦点
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => FocusAndSelectAll(sender as TextBox)));
        }
    }

    private void OnTitleEditBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ColumnViewModel column })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                column.CommitTitleEdit();
                e.Handled = true;
                break;

            case Key.Escape:
                column.CancelTitleEdit();
                e.Handled = true;
                break;
        }
    }

    private void OnTitleEditBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ColumnViewModel column })
        {
            column.CommitTitleEdit();
        }
    }

    private void OnDeleteColumnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ColumnViewModel column })
        {
            return;
        }

        if (_viewModel.Columns.Count <= 1)
        {
            MessageBox.Show(this, "至少要留一栏。", "删不了", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var noteCount = column.Notes.Count;
        if (noteCount > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"「{column.Title}」里还有 {noteCount} 条，一起删掉？",
                "删除这一栏",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _viewModel.RemoveColumn(column);
    }

    private void OnDraftPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ColumnViewModel column })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                // 回车提交并留在原输入框，可以连着敲下一行
                column.AddDraft();
                e.Handled = true;
                break;

            case Key.Escape:
                column.DraftText = string.Empty;
                e.Handled = true;
                break;
        }
    }

    // ---------- 条目 ----------

    private void OnNoteRowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: NoteViewModel note } row)
        {
            return;
        }

        _draggingNote = note;
        _dragStart = e.GetPosition(this);
        row.Focus();
    }

    private void OnNoteRowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingNote is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var note = _draggingNote;
        _draggingNote = null;

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(NoteDataFormat, note), DragDropEffects.Move);
        }
        finally
        {
            ClearDropMarks();
        }
    }

    private void OnNoteRowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: NoteViewModel note })
        {
            return;
        }

        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _draggingNote = null;
        note.BeginEdit();
        e.Handled = true;
    }

    private void OnNoteEditBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 折叠状态下 Loaded 也会触发，只有真正显示出来才抢焦点
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => FocusAndSelectAll(sender as TextBox)));
        }
    }

    private void OnNoteEditBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: NoteViewModel note } box)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Shift:
                // Shift+Enter 在条目内换行（WPF TextBox 只认不带 Ctrl 的 Enter）
                e.Handled = false;
                break;

            case Key.Enter:
                note.CommitEdit();
                FocusNoteRow(box);
                e.Handled = true;
                break;

            case Key.Escape:
                note.CancelEdit();
                FocusNoteRow(box);
                e.Handled = true;
                break;
        }
    }

    private void OnNoteEditBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: NoteViewModel note })
        {
            note.CommitEdit();
        }
    }

    private void OnDeleteNoteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteViewModel note })
        {
            _viewModel.RemoveNote(note);
        }
    }

    private void OnNotesListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is not ItemsControl list)
        {
            return;
        }

        var row = FindNoteRow(e.OriginalSource as DependencyObject);
        if (row?.DataContext is not NoteViewModel note)
        {
            return;
        }

        var index = IndexOfRow(list, row);

        switch (e.Key)
        {
            case Key.Up:
                FocusRowAt(list, index - 1);
                e.Handled = true;
                break;

            case Key.Down:
                FocusRowAt(list, index + 1);
                e.Handled = true;
                break;

            case Key.Space:
                note.Toggle();
                e.Handled = true;
                break;

            case Key.Enter:
                note.BeginEdit();
                e.Handled = true;
                break;
        }
    }

    // ---------- 拖拽落位 ----------

    private void OnNotesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(NoteDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var resolved = ResolveListAndColumn(sender);
        if (resolved is null)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var (list, column) = resolved.Value;
        var index = ComputeInsertIndex(list, e.GetPosition(list));
        ShowDropMarks(column, index);

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnNotesDragLeave(object sender, DragEventArgs e) => ClearDropMarks();

    private void OnNotesDrop(object sender, DragEventArgs e)
    {
        var resolved = ResolveListAndColumn(sender);

        if (!e.Data.GetDataPresent(NoteDataFormat) ||
            e.Data.GetData(NoteDataFormat) is not NoteViewModel note ||
            resolved is null)
        {
            ClearDropMarks();
            return;
        }

        var (list, column) = resolved.Value;
        var index = ComputeInsertIndex(list, e.GetPosition(list));

        var source = _viewModel.Columns.FirstOrDefault(c => c.Notes.Contains(note));
        var sourceIndex = source?.Notes.IndexOf(note) ?? -1;

        // 同栏内往后拖时，移走会让目标行前移一位，所以要减一
        if (ReferenceEquals(source, column) && sourceIndex >= 0 && index > sourceIndex)
        {
            index--;
        }

        _viewModel.MoveNote(note, column, index);
        ClearDropMarks();
        e.Handled = true;
    }

    private static (ItemsControl List, ColumnViewModel Column)? ResolveListAndColumn(object sender)
    {
        var list = sender as ItemsControl ?? FindDescendant<ItemsControl>(sender as DependencyObject);
        if (list?.DataContext is not ColumnViewModel column)
        {
            return null;
        }

        return (list, column);
    }

    private static int ComputeInsertIndex(ItemsControl list, Point position)
    {
        var index = 0;

        foreach (var item in list.Items)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
            {
                var top = container.TranslatePoint(new Point(0, 0), list).Y;
                if (position.Y < top + container.ActualHeight / 2)
                {
                    return index;
                }
            }

            index++;
        }

        return list.Items.Count;
    }

    private void ShowDropMarks(ColumnViewModel column, int index)
    {
        ClearDropMarks();

        if (index >= column.Notes.Count)
        {
            // 落到末尾时没有「下一行」可以划线，用整栏高亮代替
            column.IsDropTarget = true;
            return;
        }

        column.Notes[index].IsDropBefore = true;
    }

    private void ClearDropMarks()
    {
        foreach (var column in _viewModel.Columns)
        {
            column.IsDropTarget = false;

            foreach (var note in column.Notes)
            {
                note.IsDropBefore = false;
            }
        }
    }

    // ---------- 视觉树小工具 ----------

    private static void FocusAndSelectAll(TextBox? box)
    {
        if (box is null)
        {
            return;
        }

        box.Focus();
        box.SelectAll();
        box.CaretIndex = box.Text.Length;
    }

    private void FocusNoteRow(DependencyObject from)
    {
        var row = FindNoteRow(from);
        row?.Focus();
    }

    private static int IndexOfRow(ItemsControl list, DependencyObject row)
    {
        var container = FindAncestor<ContentPresenter>(row);
        return container is null ? -1 : list.ItemContainerGenerator.IndexFromContainer(container);
    }

    private static void FocusRowAt(ItemsControl list, int index)
    {
        if (index < 0 || index >= list.Items.Count)
        {
            return;
        }

        if (list.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
        {
            FindDescendantByName<FrameworkElement>(container, "NoteRow")?.Focus();
        }
    }

    private static FrameworkElement? FindNoteRow(DependencyObject? start)
    {
        var current = start;

        while (current is not null)
        {
            if (current is FrameworkElement { Name: "NoteRow" } element)
            {
                return element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;

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

    private static T? FindDescendant<T>(DependencyObject? start) where T : DependencyObject
    {
        if (start is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(start);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);

            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindDescendantByName<T>(DependencyObject? start, string name) where T : FrameworkElement
    {
        if (start is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(start);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);

            if (child is T match && match.Name == name)
            {
                return match;
            }

            var nested = FindDescendantByName<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
