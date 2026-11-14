using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wantodo.App.ViewModels;
using Wantodo.Core;

namespace Wantodo.App;

/// <summary>
/// 「想做 ⇄ 卡在什么地方」对照表。文件就是数据根下的 `board\blockers.md`，
/// 可以被 Obsidian / 记事本直接打开；主界面里悬停同名条目的原因就来自这里。
/// </summary>
public partial class BlockersWindow : Window
{
    private const string NoteDataFormat = "wantodo/note";

    private readonly BlockersViewModel _viewModel;
    private readonly DispatcherTimer _saveTimer;
    private readonly MainWindow _main;
    private bool _saving;

    public BlockersWindow(MainWindow main, string boardDirectory)
    {
        InitializeComponent();

        _main = main;
        _viewModel = new BlockersViewModel(new BlockersStore(boardDirectory));
        DataContext = _viewModel;

        PathText.Text = _viewModel.FilePath;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _saveTimer.Tick += (_, _) => Flush();

        foreach (var row in _viewModel.Rows)
        {
            row.PropertyChanged += OnRowChanged;
        }

        Loaded += (_, _) => FocusFirstRow();
        Closed += (_, _) => Flush();
    }

    /// <summary>把主界面拖过来的条目加成新行；返回是否成功。</summary>
    public bool AddFromText(string want, string blocker = "")
    {
        if (string.IsNullOrWhiteSpace(want))
        {
            return false;
        }

        var row = _viewModel.AddRow(want.Trim(), blocker);
        row.PropertyChanged += OnRowChanged;
        ScheduleSave();
        FocusRow(row);
        return true;
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ScheduleSave();
        _main.RefreshBlockerHints();
    }

    private void ScheduleSave()
    {
        if (_saving)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Flush()
    {
        _saveTimer.Stop();
        _saving = true;
        try
        {
            _viewModel.Save();
            _main.RefreshBlockerHints();
            StatusText.Text = $"已保存 · {DateTime.Now:HH:mm:ss} · 共 {_viewModel.Rows.Count} 行";
        }
        catch (IOException ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        var row = _viewModel.AddRow(string.Empty);
        row.PropertyChanged += OnRowChanged;
        FocusRow(row);
    }

    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BlockerRowViewModel row })
        {
            row.PropertyChanged -= OnRowChanged;
            _viewModel.RemoveRow(row);
            ScheduleSave();
            _main.RefreshBlockerHints();
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + _viewModel.FilePath + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "打不开资源管理器：" + ex.Message;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ---------- 拖放：从主界面拖条目进来 ----------

    private void OnRowsDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(NoteDataFormat) || e.Data.GetDataPresent(DataFormats.Text)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRowsDrop(object sender, DragEventArgs e)
    {
        string? text = null;

        if (e.Data.GetData(NoteDataFormat) is NoteViewModel note)
        {
            text = note.Text;
        }
        else if (e.Data.GetDataPresent(DataFormats.Text))
        {
            text = e.Data.GetData(DataFormats.Text) as string;
        }

        DropOverlay.Visibility = Visibility.Collapsed;
        AcceptDroppedText(text);
        e.Handled = true;
    }

    // ---------- 整窗落点：条目拖到窗口任意位置都算（不必对准表格） ----------

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(NoteDataFormat))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        DropOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(NoteDataFormat))
        {
            return;
        }

        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (e.Data.GetData(NoteDataFormat) is NoteViewModel note)
        {
            AcceptDroppedText(note.Text);
        }
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e) =>
        DropOverlay.Visibility = Visibility.Collapsed;

    private void AcceptDroppedText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) && AddFromText(text))
        {
            StatusText.Text = $"已加一行：{text}（把「卡在什么地方」补上，主界面悬停就能看到）";
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void FocusFirstRow()
    {
        if (_viewModel.Rows.Count > 0)
        {
            FocusRow(_viewModel.Rows[0]);
        }
    }

    private void FocusRow(BlockerRowViewModel row)
    {
        if (RowsHost.ItemContainerGenerator.ContainerFromItem(row) is ContentPresenter presenter)
        {
            presenter.ApplyTemplate();
            var box = FindDescendant<TextBox>(presenter);
            box?.Focus();
            box?.SelectAll();
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var found = FindDescendant<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
