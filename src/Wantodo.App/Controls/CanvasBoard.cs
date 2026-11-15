using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wantodo.App.Dialogs;
using Wantodo.App.Windows;
using Wantodo.Core;

namespace Wantodo.App.Controls;

/// <summary>
/// 形态 B 的自由画布：卡片是绝对定位的 Border，拖动与缩放都在这层处理。
/// 数据由 <see cref="CanvasDocument"/> 持有；写盘交给宿主（0.7.0 起并入 <c>data.json</c>，不再有第二份文件）。
/// </summary>
public sealed class CanvasBoard : Canvas
{
    private const double GridStep = 8;
    private const int UndoLimit = 20;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly Dictionary<string, Border> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _timerTexts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>图片卡没有常驻边框：未选中时 BorderThickness 必须回到 0。</summary>
    private readonly HashSet<string> _framelessCards = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待办卡选栏目用；由宿主（面板/桌面）提供全局栏目。</summary>
    public Func<IReadOnlyList<NoteColumn>>? ColumnsProvider { get; set; }

    /// <summary>待办卡「＋ 新建栏目」用；返回新栏 id，取消返回 null。</summary>
    public Func<string?, string?>? ColumnCreator { get; set; }

    private CanvasDocument _document = new();
    private Action<CanvasDocument>? _persist;
    private string? _imagesDir;
    private CanvasItem? _selected;
    private DispatcherTimer? _ticker;

    private FrameworkElement? _dragCard;
    private Point _dragOrigin;
    private double _startX;
    private double _startY;

    private FrameworkElement? _resizeGrip;
    private double _startW;
    private double _startH;
    private double _resizeOriginX;
    private double _resizeOriginY;

    public CanvasBoard()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Focusable = true;
        AllowDrop = true;

        Drop += OnDrop;
        DragOver += OnDragOver;
        PreviewKeyDown += OnPreviewKeyDown;
        MouseDown += (_, _) => Focus();
    }

    public CanvasDocument Document => _document;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? StatusChanged;

    /// <param name="document">数据源；0.7.0 起就是 <c>Board.Canvas</c> 那份实例。</param>
    /// <param name="persist">落盘回调：宿主把文档挂回 Board 并触发 600 ms 合并保存。</param>
    /// <param name="imagesDir">画布图片副本目录。</param>
    public void Attach(CanvasDocument document, Action<CanvasDocument>? persist, string? imagesDir)
    {
        _document = document ?? new CanvasDocument();
        _persist = persist;
        _imagesDir = imagesDir;
        Rebuild();
    }

    public void Rebuild()
    {
        Children.Clear();
        _cards.Clear();
        _timerTexts.Clear();
        _framelessCards.Clear();
        foreach (var item in _document.Items.OrderBy(i => i.ZIndex))
        {
            var card = BuildCard(item);
            Children.Add(card);
            _cards[item.Id] = card;
            Select(item, item.Id == _selected?.Id, rebuild: false);
        }

        EnsureTicker();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>在 <paramref name="centerX"/> 附近插入一张新卡片。</summary>
    public CanvasItem? Add(string kind, double centerX, double centerY)
    {
        PushUndo();
        var item = _document.Add(kind, Math.Max(0, centerX - 110), Math.Max(0, centerY - 80));
        switch (kind)
        {
            case "timer":
                item.Payload["seconds"] = CanvasClock.DefaultCountdownSeconds.ToString(CultureInfo.InvariantCulture);
                item.Payload["mode"] = CanvasClock.ModeKey(TimerMode.Countdown);
                item.Payload["startedAt"] = string.Empty;
                item.Payload["fired"] = string.Empty;
                break;
            case "image":
                item.Payload["path"] = string.Empty;
                break;
            case "todo":
                item.Payload["columnId"] = string.Empty;
                break;
        }

        Mount(item);
        Save();
        return item;
    }

    public CanvasItem? AddTimer(TimerMode mode, int seconds, double centerX, double centerY)
    {
        PushUndo();
        var item = _document.Add("timer", Math.Max(0, centerX - 110), Math.Max(0, centerY - 80));
        item.Payload["mode"] = CanvasClock.ModeKey(mode);
        item.Payload["seconds"] = Math.Max(0, seconds).ToString(CultureInfo.InvariantCulture);
        item.Payload["startedAt"] = string.Empty;
        item.Payload["fired"] = string.Empty;
        Mount(item);
        Save();
        return item;
    }

    public CanvasItem? AddImage(string sourcePath, double centerX, double centerY)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        PushUndo();
        var item = _document.Add("image", Math.Max(0, centerX - 110), Math.Max(0, centerY - 80));
        item.Payload["path"] = CopyImage(sourcePath, item.Id);
        Mount(item);
        Save();
        return item;
    }

    public CanvasItem? AddNoteFromText(string text, double centerX, double centerY)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        PushUndo();
        var item = _document.Add("note", Math.Max(0, centerX - 110), Math.Max(0, centerY - 80));
        item.Payload["text"] = text;
        Mount(item);
        Save();
        return item;
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Snapshot());
        Restore(_undo.Pop());
        Save();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Snapshot());
        Restore(_redo.Pop());
        Save();
    }

    public void DeleteSelected()
    {
        if (_selected is null)
        {
            return;
        }

        PushUndo();
        var id = _selected.Id;
        _document.Items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        _selected = null;
        Rebuild();
        Save();
    }

    private void Mount(CanvasItem item)
    {
        var card = BuildCard(item);
        Children.Add(card);
        _cards[item.Id] = card;
        _selected = item;
        Select(item, true, rebuild: false);
        card.BringIntoView();
    }

    private Border BuildCard(CanvasItem item)
    {
        if (string.Equals(item.Kind, "image", StringComparison.OrdinalIgnoreCase))
        {
            return BuildImageCard(item);
        }

        var card = new Border
        {
            Width = item.W,
            Height = item.H,
            Background = (Brush)FindResource("SurfaceCard"),
            Tag = item,
            SnapsToDevicePixels = true,
        };
        card.SetResourceReference(Border.CornerRadiusProperty, "ThemeCardCornerRadius");
        card.SetResourceReference(Border.BorderThicknessProperty, "ThemeCardBorderThickness");
        card.SetResourceReference(Border.BorderBrushProperty, "ThemeCardBorderBrush");

        Canvas.SetLeft(card, item.X);
        Canvas.SetTop(card, item.Y);
        Panel.SetZIndex(card, item.ZIndex);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(item.Get("title")) ? KindLabel(item.Kind) : item.Get("title")!,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(4, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondary"),
            CaretBrush = (Brush)FindResource("TextSecondary"),
            ToolTip = "点一下可以改标题",
        };
        title.TextChanged += (s, _) =>
        {
            var box = (TextBox)s!;
            item.Payload["title"] = box.Text;
        };
        title.GotKeyboardFocus += (_, _) => PushUndo();
        title.LostKeyboardFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(item.Get("title")))
            {
                item.Payload.Remove("title");
            }

            Save();
        };

        var close = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (Brush)FindResource("TextMuted"),
            Cursor = Cursors.Hand,
            ToolTip = "删除这张卡片",
        };
        close.Click += (_, _) =>
        {
            _selected = item;
            DeleteSelected();
        };

        header.Children.Add(title);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var bodyHost = new Grid { Margin = new Thickness(10, 6, 10, 8) };
        switch (item.Kind)
        {
            case "note":
                BuildNoteBody(item, bodyHost);
                break;
            case "todo":
                BuildTodoBody(item, bodyHost);
                break;
            case "image":
                BuildImageBody(item, bodyHost);
                break;
            case "timer":
                BuildTimerBody(item, bodyHost);
                break;
            case "link":
                BuildLegacyLinkBody(item, bodyHost);
                break;
            default:
                AddPlainText(item, bodyHost);
                break;
        }

        Grid.SetRow(bodyHost, 1);

        // 缩放把：透明命中区 14×14，里面画两条斜线当图标；只有悬停时才亮，
        // 平时不抢视觉（之前是一块实心灰方块，看着像渲染坏了）。
        var gripHost = new Grid
        {
            Width = 14,
            Height = 14,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 1),
            Cursor = Cursors.SizeNWSE,
            ToolTip = "拖动缩放",
            Tag = item,
            Opacity = 0.45,
        };
        gripHost.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 3,11 L 11,3 M 7,12 L 12,7"),
            Stroke = (Brush)FindResource("TextMuted"),
            StrokeThickness = 1.2,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        });
        gripHost.MouseEnter += (_, _) => gripHost.Opacity = 0.9;
        gripHost.MouseLeave += (_, _) => gripHost.Opacity = 0.45;
        Grid.SetRowSpan(gripHost, 2);

        grid.Children.Add(header);
        grid.Children.Add(bodyHost);
        grid.Children.Add(gripHost);
        card.Child = grid;

        card.MouseLeftButtonDown += OnCardMouseDown;
        card.MouseMove += OnCardMouseMove;
        card.MouseLeftButtonUp += OnCardMouseUp;
        gripHost.MouseLeftButtonDown += OnGripMouseDown;
        gripHost.MouseMove += OnGripMouseMove;
        gripHost.MouseLeftButtonUp += OnGripMouseUp;
        return card;
    }

    /// <summary>
    /// 图片卡：不画边框和标题栏，图片铺满圆角区域，像直接贴在画布上；
    /// 只有悬停/选中时才有浮层标题、删除与 2 px 高光。
    /// </summary>
    private Border BuildImageCard(CanvasItem item)
    {
        _framelessCards.Add(item.Id);

        var image = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
        string? path = ResolveImage(item);
        if (path is not null && File.Exists(path))
        {
            image.Source = LoadBitmap(path);
        }

        var clipHost = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            ClipToBounds = true,
        };
        clipHost.Child = image;
        clipHost.SizeChanged += (_, e) =>
        {
            clipHost.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
        };

        var fallback = new TextBlock
        {
            Text = "（未选择图片）\n拖入图片，或按 Ctrl+V 粘贴",
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMuted"),
            IsHitTestVisible = false,
        };

        var overlay = new Grid
        {
            Opacity = 0,
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Top,
            Height = 26,
        };
        overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBox
        {
            Text = item.Get("title") ?? "图片",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "点一下可以改标题",
        };
        title.TextChanged += (s, _) => item.Payload["title"] = ((TextBox)s!).Text;
        title.GotKeyboardFocus += (_, _) => PushUndo();
        title.LostKeyboardFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(item.Get("title")))
            {
                item.Payload.Remove("title");
            }

            Save();
        };

        var close = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
            ToolTip = "删除这张卡片",
        };
        close.Click += (_, _) =>
        {
            _selected = item;
            DeleteSelected();
        };

        overlay.Children.Add(title);
        Grid.SetColumn(close, 1);
        overlay.Children.Add(close);

        var grid = new Grid();
        grid.Children.Add(clipHost);
        grid.Children.Add(fallback);
        grid.Children.Add(overlay);

        var card = new Border
        {
            Width = item.W,
            Height = item.H,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Tag = item,
            Child = grid,
            SnapsToDevicePixels = true,
            ToolTip = path is null ? null : "双击或右键打开原图",
        };

        Canvas.SetLeft(card, item.X);
        Canvas.SetTop(card, item.Y);
        Panel.SetZIndex(card, item.ZIndex);

        card.MouseEnter += (_, _) => overlay.Opacity = 1;
        card.MouseLeave += (_, _) => overlay.Opacity = 0;
        card.MouseLeftButtonDown += OnCardMouseDown;
        card.MouseMove += OnCardMouseMove;
        card.MouseLeftButtonUp += OnCardMouseUp;
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OpenImage(path);
                e.Handled = true;
            }
        };
        card.MouseRightButtonUp += (_, e) =>
        {
            OpenImage(path);
            e.Handled = true;
        };

        return card;
    }

    private static void OpenImage(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            AppLog.Warn($"打开图片失败：{path}；{ex.Message}");
        }
    }

    private void BuildNoteBody(CanvasItem item, Grid host)
    {
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var editor = new TextBox
        {
            Text = item.Get("text") ?? string.Empty,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            FontSize = 12,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Foreground = (Brush)FindResource("TextPrimary"),
            CaretBrush = (Brush)FindResource("TextPrimary"),
            Tag = item,
        };

        var hint = new TextBlock
        {
            Text = "（空）",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMuted"),
            IsHitTestVisible = false,
            Visibility = editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
        };

        var links = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

        editor.TextChanged += (s, _) =>
        {
            var box = (TextBox)s!;
            item.Payload["text"] = box.Text;
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            FillLinks(links, box.Text);
        };
        editor.GotKeyboardFocus += (_, _) => PushUndo();
        editor.LostKeyboardFocus += (_, _) => Save();

        FillLinks(links, editor.Text);

        var layer = new Grid();
        layer.Children.Add(editor);
        layer.Children.Add(hint);
        Grid.SetRow(layer, 0);
        Grid.SetRow(links, 1);
        host.Children.Add(layer);
        host.Children.Add(links);
    }

    private void BuildTodoBody(CanvasItem item, Grid host)
    {
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        IReadOnlyList<NoteColumn> columns = ColumnsProvider?.Invoke() ?? Array.Empty<NoteColumn>();
        string? columnId = item.Get("columnId");

        var combo = new ComboBox
        {
            FontSize = 12,
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = item,
            ToolTip = "选择这一条要放进哪一栏",
        };

        foreach (var column in columns)
        {
            combo.Items.Add(new ColumnChoice(column.Id, column.Title));
        }

        combo.Items.Add(ColumnChoice.CreateNew);
        combo.SelectedItem = combo.Items.OfType<ColumnChoice>()
            .FirstOrDefault(c => string.Equals(c.Id, columnId, StringComparison.Ordinal));

        // 没选栏目时 ComboBox 会显示成一块空白（上一版用户截图里就是这样）。垫一层占位字，
        // 选中后隐藏；不影响下拉，也不吃点击。
        var placeholder = new TextBlock
        {
            Text = "选择栏目…",
            Foreground = (Brush)FindResource("TextMuted"),
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = combo.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed,
        };

        var comboHost = new Grid();
        comboHost.Children.Add(combo);
        comboHost.Children.Add(placeholder);

        var preview = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Get("text")) ? "（这条待办没有正文）" : item.Get("text")!,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("TextPrimary"),
        };

        combo.SelectionChanged += (_, _) =>
        {
            placeholder.Visibility = combo.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
            if (combo.SelectedItem is not ColumnChoice choice)
            {
                return;
            }

            string? targetId = choice.Id;
            if (choice.IsCreateNew)
            {
                targetId = ColumnCreator?.Invoke(null);
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    combo.SelectedItem = combo.Items.OfType<ColumnChoice>()
                        .FirstOrDefault(c => string.Equals(c.Id, item.Get("columnId"), StringComparison.Ordinal));
                    return;
                }

                // 新建的栏马上出现在候选列表里，不需要重建整张卡。
                combo.Items.Insert(combo.Items.Count - 1, new ColumnChoice(targetId, ColumnCreatorTitle ?? "新栏目"));
            }

            item.Payload["columnId"] = targetId;
            Save();
        };

        var links = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        FillLinks(links, item.Get("text"));

        Grid.SetRow(comboHost, 0);
        Grid.SetRow(preview, 1);
        Grid.SetRow(links, 2);
        host.Children.Add(comboHost);
        host.Children.Add(preview);
        host.Children.Add(links);
    }

    /// <summary>新建栏目后显示的名字（由面板在创建后写入，仅用于候选列表刷新）。</summary>
    public string? ColumnCreatorTitle { get; set; }

    private sealed record ColumnChoice(string Id, string Title, bool IsCreateNew = false)
    {
        public static ColumnChoice CreateNew { get; } = new("__new__", "＋ 新建栏目", true);

        public override string ToString() => Title;
    }

    private void BuildImageBody(CanvasItem item, Grid host)
    {
        var path = ResolveImage(item);
        if (path is not null && File.Exists(path))
        {
            var image = new Image
            {
                Source = LoadBitmap(path),
                Stretch = Stretch.Uniform,
                ToolTip = path,
            };
            host.Children.Add(image);
            return;
        }

        host.Children.Add(new TextBlock
        {
            Text = "（未选择图片）\n把图片拖到这里，或按 Ctrl+V 粘贴",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMuted"),
        });
    }

    private void BuildTimerBody(CanvasItem item, Grid host)
    {
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var mode = CanvasClock.ParseMode(item.Get("mode"));
        int duration = int.TryParse(item.Get("seconds"), out int seconds) ? seconds : CanvasClock.DefaultSeconds(mode);

        host.Children.Add(new TextBlock
        {
            Text = CanvasClock.ModeLabel(mode),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMuted"),
        });

        var display = new TextBlock
        {
            Text = CanvasClock.Format(CanvasClock.DisplaySeconds(
                mode, duration, item.Get("startedAt"), BaseElapsed(item), DateTimeOffset.Now)),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _timerTexts[item.Id] = display;
        Grid.SetRow(display, 1);
        host.Children.Add(display);

        bool running = IsRunning(item);
        bool paused = string.Equals(item.Get("state"), "paused", StringComparison.OrdinalIgnoreCase);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var start = new Button
        {
            Content = running ? "暂停" : paused ? "继续" : "开始",
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            ToolTip = running ? "暂停" : "开始计时",
        };
        start.Click += (_, _) =>
        {
            if (running)
            {
                PauseTimer(item);
            }
            else
            {
                StartTimer(item);
            }
        };

        var reset = new Button
        {
            Content = "↺",
            Width = 26,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 11,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "清零",
        };
        reset.Click += (_, _) => ResetTimer(item);

        var setup = new Button
        {
            Content = "设置",
            Height = 22,
            Padding = new Thickness(6, 0, 6, 0),
            FontSize = 11,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "换玩法或时长",
        };
        setup.Click += (_, _) => ReconfigureTimer(item);

        actions.Children.Add(start);
        actions.Children.Add(reset);
        actions.Children.Add(setup);
        Grid.SetRow(actions, 2);
        host.Children.Add(actions);
    }

    private void BuildLegacyLinkBody(CanvasItem item, Grid host)
    {
        var url = item.Get("url");
        host.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(url) ? "（未设置链接）" : url,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimary"),
        });
    }

    private void AddPlainText(CanvasItem item, Grid host)
    {
        host.Children.Add(new TextBlock
        {
            Text = item.Kind,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimary"),
        });
    }

    private void FillLinks(Panel panel, string? text)
    {
        panel.Children.Clear();
        foreach (var url in TextLinks.Extract(text))
        {
            var button = new Button
            {
                Content = url,
                Margin = new Thickness(0, 0, 6, 2),
                Padding = new Thickness(4, 0, 4, 0),
                FontSize = 11,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (Brush)FindResource("Accent"),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = "在浏览器里打开",
            };
            button.Click += (_, e) =>
            {
                OpenUrl(url);
                e.Handled = true;
            };
            panel.Children.Add(button);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"打开链接失败：{url}：{ex.Message}");
        }
    }

    private void StartTimer(CanvasItem item)
    {
        item.Payload["startedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        item.Payload["fired"] = string.Empty;
        item.Payload["state"] = "running";
        item.Payload["mode"] = CanvasClock.ModeKey(CanvasClock.ParseMode(item.Get("mode")));
        Save();
        Rebuild();
    }

    private void PauseTimer(CanvasItem item)
    {
        var mode = CanvasClock.ParseMode(item.Get("mode"));
        int duration = int.TryParse(item.Get("seconds"), out int seconds) ? seconds : CanvasClock.DefaultSeconds(mode);
        int baseElapsed = int.TryParse(item.Get("elapsed"), out int elapsed) ? elapsed : 0;
        DateTimeOffset started = DateTimeOffset.TryParse(
            item.Get("startedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.Now;

        item.Payload["elapsed"] = CanvasClock
            .AccumulatedSeconds(mode, duration, baseElapsed, started, DateTimeOffset.Now)
            .ToString(CultureInfo.InvariantCulture);
        item.Payload["startedAt"] = string.Empty;
        item.Payload["state"] = "paused";
        Save();
        Rebuild();
    }

    private static bool IsRunning(CanvasItem item) => !string.IsNullOrWhiteSpace(item.Get("startedAt"));

    private static int BaseElapsed(CanvasItem item) => int.TryParse(item.Get("elapsed"), out int elapsed) ? Math.Max(0, elapsed) : 0;

    private void ResetTimer(CanvasItem item)
    {
        item.Payload["startedAt"] = string.Empty;
        item.Payload["fired"] = string.Empty;
        item.Payload["elapsed"] = "0";
        item.Payload["state"] = "idle";
        Save();
        Rebuild();
    }

    private void ReconfigureTimer(CanvasItem item)
    {
        var mode = CanvasClock.ParseMode(item.Get("mode"));
        int seconds = int.TryParse(item.Get("seconds"), out int stored) ? stored : CanvasClock.DefaultSeconds(mode);
        var dialog = new TimerSetupDialog(mode, seconds) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        item.Payload["mode"] = CanvasClock.ModeKey(dialog.Mode);
        item.Payload["seconds"] = dialog.Seconds.ToString(CultureInfo.InvariantCulture);
        item.Payload["startedAt"] = string.Empty;
        item.Payload["fired"] = string.Empty;
        item.Payload["elapsed"] = "0";
        item.Payload["state"] = "configured";
        Save();
        Rebuild();
    }

    private void EnsureTicker()
    {
        if (_ticker is null)
        {
            _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _ticker.Tick += (_, _) => Tick();
        }

        if (!_ticker.IsEnabled)
        {
            _ticker.Start();
        }
    }

    private void Tick()
    {
        if (_document.Items.Count == 0)
        {
            return;
        }

        bool changed = false;
        var now = DateTimeOffset.Now;
        foreach (var item in _document.Items.Where(i => string.Equals(i.Kind, "timer", StringComparison.OrdinalIgnoreCase)))
        {
            var mode = CanvasClock.ParseMode(item.Get("mode"));
            int duration = int.TryParse(item.Get("seconds"), out int seconds) ? seconds : CanvasClock.DefaultSeconds(mode);
            if (_timerTexts.TryGetValue(item.Id, out var display))
            {
                display.Text = CanvasClock.Format(
                    CanvasClock.DisplaySeconds(mode, duration, item.Get("startedAt"), BaseElapsed(item), now));
            }

            if (string.IsNullOrWhiteSpace(item.Get("startedAt"))
                || string.Equals(item.Get("fired"), "1", StringComparison.Ordinal)
                || !CanvasClock.IsFinished(mode, duration, item.Get("startedAt"), BaseElapsed(item), now))
            {
                continue;
            }

            item.Payload["fired"] = "1";
            item.Payload["startedAt"] = string.Empty;
            item.Payload["state"] = "done";
            changed = true;
            var title = string.IsNullOrWhiteSpace(item.Get("title")) ? KindLabel(item.Kind) : item.Get("title")!;
            ToastWindow.Show(title, CanvasClock.ModeLabel(mode) + "到点了");
            AppSound.Chime();
        }

        if (changed)
        {
            Save();
            Rebuild();
        }
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private string? ResolveImage(CanvasItem item)
    {
        var name = item.Get("path");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Path.IsPathRooted(name) ? name : Path.Combine(_imagesDir ?? string.Empty, name);
    }

    private string CopyImage(string sourcePath, string id)
    {
        if (string.IsNullOrWhiteSpace(_imagesDir))
        {
            return sourcePath;
        }

        Directory.CreateDirectory(_imagesDir);
        string extension = Path.GetExtension(sourcePath);
        string target = Path.Combine(_imagesDir, id + extension);
        for (int n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(_imagesDir, $"{id}-{n}{extension}");
        }

        File.Copy(sourcePath, target, overwrite: false);
        return Path.GetFileName(target);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        var point = e.GetPosition(this);
        bool any = false;
        foreach (var file in files)
        {
            if (!ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            any |= AddImage(file, point.X, point.Y) is not null;
            point.Y += 24;
        }

        if (any)
        {
            e.Handled = true;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (TryPaste())
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Delete && _selected is not null && !InTextBox(e.OriginalSource))
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private bool TryPaste()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var source = Clipboard.GetImage();
                if (source is null || string.IsNullOrWhiteSpace(_imagesDir))
                {
                    return false;
                }

                Directory.CreateDirectory(_imagesDir);
                string name = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
                string path = Path.Combine(_imagesDir, name);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(stream);
                    stream.Flush(true);
                }

                var center = CenterOfViewport();
                AddImage(path, center.X, center.Y);
                return true;
            }

            if (Clipboard.ContainsText() && !InTextBox(Keyboard.FocusedElement))
            {
                var text = Clipboard.GetText();
                var center = CenterOfViewport();
                return AddNoteFromText(text, center.X, center.Y) is not null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn($"粘贴到画板失败：{ex.GetType().Name}: {ex.Message}");
        }

        return false;
    }

    private Point CenterOfViewport()
    {
        if (Parent is ScrollViewer scroller)
        {
            return new Point(
                scroller.HorizontalOffset + scroller.ViewportWidth / 2,
                scroller.VerticalOffset + scroller.ViewportHeight / 2);
        }

        return new Point(ActualWidth / 2, ActualHeight / 2);
    }

    private static bool InTextBox(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is TextBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static string KindLabel(string kind) => kind switch
    {
        "todo" => "待办",
        "timer" => "计时",
        "link" => "链接",
        "image" => "图片",
        _ => "记事",
    };

    private void Select(CanvasItem item, bool selected, bool rebuild)
    {
        if (!_cards.TryGetValue(item.Id, out var card))
        {
            return;
        }

        bool frameless = _framelessCards.Contains(item.Id);
        if (selected)
        {
            card.BorderBrush = (Brush)FindResource("BorderActive");
            card.BorderThickness = new Thickness(2);
        }
        else if (frameless)
        {
            card.BorderThickness = new Thickness(0);
        }
        else
        {
            card.SetResourceReference(Border.BorderBrushProperty, "ThemeCardBorderBrush");
            card.SetResourceReference(Border.BorderThicknessProperty, "ThemeCardBorderThickness");
        }
        if (selected)
        {
            _selected = item;
        }
    }

    private void BringToFront(CanvasItem item)
    {
        if (_document.Items.Count == 0)
        {
            return;
        }

        var max = _document.Items.Max(i => i.ZIndex);
        if (item.ZIndex >= max)
        {
            return;
        }

        item.ZIndex = Math.Min(999, max + 1);
        if (_cards.TryGetValue(item.Id, out var card))
        {
            Panel.SetZIndex(card, item.ZIndex);
        }
    }

    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card || card.Tag is not CanvasItem item)
        {
            return;
        }

        if (e.OriginalSource is Button || InTextBox(e.OriginalSource))
        {
            return;
        }

        Select(item, true, rebuild: false);
        BringToFront(item);
        _dragCard = card;
        _dragOrigin = e.GetPosition(this);
        _startX = item.X;
        _startY = item.Y;
        card.CaptureMouse();
        e.Handled = true;
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCard is null || sender != _dragCard || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (_dragCard.Tag is not CanvasItem item)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragOrigin;
        item.X = Math.Max(0, Snap(_startX + delta.X));
        item.Y = Math.Max(0, Snap(_startY + delta.Y));
        Canvas.SetLeft(_dragCard, item.X);
        Canvas.SetTop(_dragCard, item.Y);
        e.Handled = true;
    }

    private void OnCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCard is null)
        {
            return;
        }

        _dragCard.ReleaseMouseCapture();
        _dragCard = null;
        Save();
        e.Handled = true;
    }

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement grip || grip.Tag is not CanvasItem item)
        {
            return;
        }

        _resizeGrip = grip;
        _resizeOriginX = e.GetPosition(this).X;
        _resizeOriginY = e.GetPosition(this).Y;
        _startW = item.W;
        _startH = item.H;
        grip.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeGrip is null || sender != _resizeGrip || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (_resizeGrip.Tag is not CanvasItem item || !_cards.TryGetValue(item.Id, out var card))
        {
            return;
        }

        var delta = e.GetPosition(this);
        var w = Math.Clamp(_startW + (delta.X - _resizeOriginX), CanvasItem.MinWidth, CanvasItem.MaxWidth);
        var h = Math.Clamp(_startH + (delta.Y - _resizeOriginY), CanvasItem.MinHeight, CanvasItem.MaxHeight);
        item.W = Snap(w);
        item.H = Snap(h);
        card.Width = item.W;
        card.Height = item.H;
        e.Handled = true;
    }

    private void OnGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeGrip is null)
        {
            return;
        }

        _resizeGrip.ReleaseMouseCapture();
        _resizeGrip = null;
        Save();
        e.Handled = true;
    }

    private static double Snap(double value) => Math.Round(value / GridStep, MidpointRounding.AwayFromZero) * GridStep;

    private string Snapshot() => JsonSerializer.Serialize(_document, SnapshotOptions);

    private void Restore(string json)
    {
        var restored = JsonSerializer.Deserialize<CanvasDocument>(json, SnapshotOptions);
        if (restored is null)
        {
            return;
        }

        restored.Normalize();
        _document = restored;
        _selected = null;
        Rebuild();
    }

    private void PushUndo()
    {
        _undo.Push(Snapshot());
        if (_undo.Count > UndoLimit)
        {
            var newest = _undo.ToArray().Take(UndoLimit).ToArray();
            _undo.Clear();
            for (var i = newest.Length - 1; i >= 0; i--)
            {
                _undo.Push(newest[i]);
            }
        }

        _redo.Clear();
    }

    private void Save()
    {
        if (_persist is null)
        {
            return;
        }

        try
        {
            _persist(_document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"画布保存失败：{ex.GetType().Name}: {ex.Message}");
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>「叮铃」只响一次：系统提示音最省事，也不引第三方音频文件。</summary>
internal static class AppSound
{
    public static void Chime()
    {
        try
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Warn($"提示音播放失败：{ex.Message}");
        }
    }
}
