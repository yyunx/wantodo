using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wantodo.App.Notes;
using Wantodo.Core;

namespace Wantodo.App;

/// <summary>
/// 随心记：左边列表、右边编辑。文件是唯一事实来源——这里只负责把改动写回磁盘，
/// 以及监听外部编辑器改文件后刷新列表（见 docs/design/data-root.md 的 notes 布局）。
/// </summary>
public partial class NotesWindow : Window
{
    private readonly NotesVault _vault;
    private readonly DataPaths _paths;
    private readonly Func<string> _defaultFormat;
    private readonly ObservableCollection<NoteListItem> _items = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _reloadTimer;
    private FileSystemWatcher? _watcher;

    private NoteListItem? _current;
    private bool _loading;
    private bool _saving;
    private bool _dirty;

    public NotesWindow(DataPaths paths, Func<string> defaultFormat)
    {
        _paths = paths;
        _defaultFormat = defaultFormat;
        _vault = new NotesVault(paths.NotesDir, paths.TrashDir);

        InitializeComponent();

        NotesList.ItemsSource = _items;
        RootHint.Text = paths.NotesDir;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _saveTimer.Tick += (_, _) => FlushSave();

        _reloadTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            if (_dirty)
            {
                // 用户正在打字：先别用磁盘内容盖掉编辑器，等这次输入落盘后再刷。
                _reloadTimer.Start();
                return;
            }

            if (_saving)
            {
                _reloadTimer.Start();
                return;
            }

            Reload(keepSelection: true);
        };

        Reload(keepSelection: false);
        StartWatcher();
        Closed += (_, _) => StopWatcher();
    }

    /// <summary>切到某一篇（供外部/测试调用）。</summary>
    public void SelectNote(string id)
    {
        var item = _items.FirstOrDefault(i => i.Document.Id == id);
        if (item is not null)
        {
            NotesList.SelectedItem = item;
        }
    }

    private void StartWatcher()
    {
        try
        {
            if (!Directory.Exists(_paths.NotesDir))
            {
                return;
            }

            _watcher = new FileSystemWatcher(_paths.NotesDir, "*.*")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn($"随心记监听启动失败：{ex.Message}");
        }
    }

    private void StopWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // 事件在后台线程来，切回 UI 线程去抖后重载列表。
        Dispatcher.BeginInvoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private void Reload(bool keepSelection)
    {
        var keepId = keepSelection ? _current?.Document.Id : null;

        _loading = true;
        try
        {
            _items.Clear();
            foreach (var doc in _vault.Scan())
            {
                _items.Add(new NoteListItem(doc));
            }

            NoteListItem? target = null;
            if (keepId is not null)
            {
                target = _items.FirstOrDefault(i => i.Document.Id == keepId);
            }

            target ??= _items.FirstOrDefault();
            NotesList.SelectedItem = target;
            LoadIntoEditor(target);
        }
        finally
        {
            _loading = false;
        }

        UpdateStatus();
    }

    private void LoadIntoEditor(NoteListItem? item)
    {
        _current = item;
        _dirty = false;
        _saveTimer.Stop();

        if (item is null)
        {
            TitleBox.Text = string.Empty;
            BodyBox.Text = string.Empty;
            TitleBox.IsEnabled = false;
            BodyBox.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            return;
        }

        TitleBox.IsEnabled = true;
        BodyBox.IsEnabled = true;
        DeleteButton.IsEnabled = true;
        TitleBox.Text = item.Document.Title;
        BodyBox.Text = item.Document.Body;
        BodyBox.CaretIndex = BodyBox.Text.Length;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (_dirty)
        {
            FlushSave();
        }

        LoadIntoEditor(NotesList.SelectedItem as NoteListItem);
        UpdateStatus();
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current is null)
        {
            return;
        }

        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        StatusText.Text = "正在输入…（停手 0.6 秒后自动保存）";
    }

    private void FlushSave()
    {
        _saveTimer.Stop();
        if (!_dirty || _current is null)
        {
            return;
        }

        _dirty = false;
        _saving = true;
        try
        {
            var document = _current.Document;
            // 新建时文件名还是「未命名」占位，第一次起好标题就顺手把文件改名。
            var isPlaceholder = string.Equals(document.Title, "未命名", StringComparison.Ordinal)
                || string.Equals(Path.GetFileNameWithoutExtension(document.FullPath).Split('-').Last(), "未命名", StringComparison.Ordinal);
            if (isPlaceholder && !string.IsNullOrWhiteSpace(TitleBox.Text) && !string.Equals(TitleBox.Text.Trim(), "未命名", StringComparison.Ordinal))
            {
                var renamed = _vault.Rename(document, TitleBox.Text.Trim());
                if (renamed is not null)
                {
                    document = renamed;
                }
            }

            var updated = _vault.Write(document, TitleBox.Text, BodyBox.Text);
            if (updated is null)
            {
                StatusText.Text = "保存失败：文件可能已被删除";
                return;
            }

            // 重新读一遍拿到真实的 mtime，避免列表时间不刷新。
            var reread = _vault.Read(updated.Id) ?? updated;
            _current = new NoteListItem(reread);
            _loading = true;
            try
            {
                var index = NotesList.SelectedIndex;
                if (index >= 0 && index < _items.Count)
                {
                    _items[index] = _current;
                    NotesList.SelectedIndex = index;
                }
            }
            finally
            {
                _loading = false;
            }

            UpdateStatus(saved: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"保存失败：{ex.Message}";
            AppLog.Warn($"随心记保存失败：{ex.Message}");
        }
        finally
        {
            _saving = false;
        }
    }

    private void UpdateStatus(bool saved = false)
    {
        var count = _items.Count;
        StatusText.Text = _current is null
            ? $"{count} 篇笔记 · 文件在 {_paths.NotesDir}"
            : $"{count} 篇 · {_current.Document.Id}{(saved ? " · 已保存" : string.Empty)} · 文件是唯一事实来源，外部改了会自动刷新";
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (_dirty)
        {
            FlushSave();
        }

        var format = string.Equals(_defaultFormat(), "md", StringComparison.OrdinalIgnoreCase)
            ? NoteFormat.Md
            : NoteFormat.Txt;

        var result = _vault.Create(format, "未命名");
        if (!result.Success || result.Document is null)
        {
            MessageBox.Show(this, $"建不出新笔记：{result.Error}", "随心记", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppLog.Info($"随心记新建：{result.Document.Id}");
        Reload(keepSelection: false);
        SelectNote(result.Document.Id);
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"把「{_current.Title}」移到回收目录？\n（trash\\ 里的文件不会自动删，30 天后才清理）",
            "随心记",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _dirty = false;
        _saveTimer.Stop();
        if (!_vault.Trash(_current.Document))
        {
            MessageBox.Show(this, "删除失败：文件可能已经被外部删掉了。", "随心记", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppLog.Info($"随心记删除：{_current.Document.Id}");
        Reload(keepSelection: false);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_paths.NotesDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.NotesDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"打不开目录：{ex.Message}", "随心记", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 松手太快时 DragMove 会抛，忽略即可。
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        FlushSave();
        base.OnClosing(e);
    }
}
