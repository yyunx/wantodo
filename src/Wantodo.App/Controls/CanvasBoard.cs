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

    /// <summary>按下后移动超过这个距离才算拖动；小于它一律当点击（plan §5.5）。</summary>
    private const double DragThreshold = 4;

    private readonly Dictionary<string, TitleSlot> _titles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待办卡 → 重画投影的闭包；每秒和写操作后各调一次（plan §5.5 的「1 s 内出现」）。</summary>
    private readonly Dictionary<string, Action> _todoRefreshes = new(StringComparer.OrdinalIgnoreCase);

    private readonly SurfaceUndo _undo = new();
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
    private bool _dragStarted;
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

    /// <summary>本面所属的撤销栈键（0.7.0 P1.2：每表面一条栈，计划书 §4.6）。</summary>
    private const string Surface = CanvasSurfaces.Structured;

    public bool CanUndo => _undo.CanUndo(Surface);

    public bool CanRedo => _undo.CanRedo(Surface);

    /// <summary>当前表面还能撤销几步（状态行显示）。</summary>
    public int UndoDepth => _undo.Depth(Surface);

    // —— 测试/探针钩子：真实 UI 里这些都由定时器与鼠标触发 ——

    /// <summary>立刻跑一次 <see cref="Tick"/>，探针不依赖真实 1 s 定时器。</summary>
    internal void TickForTest() => Tick();

    /// <summary>卡片是否正处于改名输入态（标题槽位里有 TextBox）。</summary>
    internal bool IsRenamingForTest => _titles.Values.Any(slot => slot.Host.Children.OfType<TextBox>().Any());

    internal static double DragThresholdForTest => DragThreshold;

    /// <summary>按 id 选中（没有鼠标的探针用它走 F2 / 改名路径）。</summary>
    internal bool SelectForTest(string id)
    {
        var item = _document.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return false;
        }

        Select(item, true, rebuild: false);
        return true;
    }

    public event EventHandler? StatusChanged;

    /// <summary>
    /// 结构化表面收到图片粘贴/拖放。计划书 §4.7：图片只存在于自由表面，
    /// 所以这里不落卡片，只把意图交给宿主（切面 + 在自由表面落位）。
    /// </summary>
    public event EventHandler? ImagePasteRequested;

    /// <summary>结构化表面被拖入图片文件；宿主负责切到自由表面并逐个插入。</summary>
    public event EventHandler<IReadOnlyList<string>>? ImageDropRequested;

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
        _titles.Clear();
        _todoRefreshes.Clear();
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
        if (!_undo.TryUndo(Surface, Snapshot(), out var json))
        {
            return;
        }

        Restore(json);
        Save();
    }

    public void Redo()
    {
        if (!_undo.TryRedo(Surface, Snapshot(), out var json))
        {
            return;
        }

        Restore(json);
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

        var title = CreateTitleHost(item, Brushes.Transparent, (Brush)FindResource("TextSecondary"), 12);

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
            image.Source = LoadBitmap(path, item.W);
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

        var title = CreateTitleHost(item, Brushes.Transparent, Brushes.White, 12);

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

    /// <summary>标题槽位：一个 Grid 里放静态 TextBlock，双击时临时换成 TextBox。</summary>
    private sealed record TitleSlot(Grid Host, TextBlock Text, Brush Foreground, double FontSize);

    private Grid CreateTitleHost(CanvasItem item, Brush background, Brush foreground, double fontSize)
    {
        var host = new Grid { Background = background, ToolTip = "双击改名（F2 也行）" };
        var text = new TextBlock
        {
            Text = TitleOf(item),
            Margin = new Thickness(6, 0, 4, 0),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2)
            {
                // 吞掉双击：既不让它冒泡成拖动，也不让单击路径进编辑
                e.Handled = true;
                BeginRename(item);
            }
        };
        host.Children.Add(text);
        _titles[item.Id] = new TitleSlot(host, text, foreground, fontSize);
        return host;
    }

    private string TitleOf(CanvasItem item)
        => string.IsNullOrWhiteSpace(item.Get("title"))
            ? (string.Equals(item.Kind, "image", StringComparison.OrdinalIgnoreCase) ? "图片" : KindLabel(item.Kind))
            : item.Get("title")!;

    /// <summary>F2：给当前选中的卡片改名（plan §5.5）。</summary>
    public void BeginRenameSelected()
    {
        if (_selected is not null)
        {
            BeginRename(_selected);
        }
    }

    private void BeginRename(CanvasItem item)
    {
        if (!_titles.TryGetValue(item.Id, out var slot))
        {
            return;
        }

        if (slot.Host.Children.OfType<TextBox>().Any())
        {
            return;
        }

        PushUndo();
        var box = new TextBox
        {
            Text = item.Get("title") ?? string.Empty,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(4, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = slot.FontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = slot.Foreground,
            CaretBrush = slot.Foreground,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        slot.Text.Visibility = Visibility.Collapsed;
        slot.Host.Children.Add(box);
        box.Focus();
        box.SelectAll();

        bool closed = false;
        void Close(bool keep)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            if (keep)
            {
                var value = box.Text.Trim();
                if (value.Length == 0)
                {
                    item.Payload.Remove("title");
                }
                else
                {
                    item.Payload["title"] = value;
                }
            }

            slot.Host.Children.Remove(box);
            slot.Text.Visibility = Visibility.Visible;
            slot.Text.Text = TitleOf(item);
            Save();
            Focus();
        }

        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Enter:
                    Close(true);
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.Escape:
                    Close(false);
                    e.Handled = true;
                    break;
            }
        };
        box.LostKeyboardFocus += (_, _) => Close(true);
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
            Foreground = (Brush)FindResource("TextMuted"),
        };

        // 待办卡 = 栏目投影（plan §5.5 / Q5）：卡片里不存条目，只把桌面的那一栏画出来。
        // 勾选和加条目是写入口，写回的还是同一个 NoteColumn。
        var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var listScroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var draft = new TextBox
        {
            FontSize = 12,
            Height = 24,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "回车添加到当前栏目",
        };

        string signature = string.Empty;

        void Refresh(bool force = false)
        {
            var column = columns.FirstOrDefault(c => string.Equals(c.Id, item.Get("columnId"), StringComparison.Ordinal));
            string current = column is null
                ? "-"
                : column.Id + "|" + column.Notes.Count + "|" + string.Join("", column.Notes.Select(n => n.Completed ? '1' : '0'))
                  + "|" + string.Join("\u001f", column.Notes.Select(n => n.Text));
            if (!force && current == signature)
            {
                return;
            }

            signature = current;
            list.Children.Clear();
            if (column is null)
            {
                list.Children.Add(Hint("选一栏后，这里显示栏目里的条目"));
                draft.IsEnabled = false;
                return;
            }

            draft.IsEnabled = true;
            int done = column.Notes.Count(n => n.Completed);
            list.Children.Add(Hint($"{column.Title} · {column.Notes.Count} 条 · 已完成 {done}"));
            if (column.Notes.Count == 0)
            {
                list.Children.Add(Hint("这一栏还没有条目"));
                return;
            }

            foreach (var note in column.Notes.Take(50))
            {
                var check = new CheckBox
                {
                    IsChecked = note.Completed,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 6, 0),
                };
                var captured = note;
                check.Checked += (_, _) => ToggleProjected(captured, true);
                check.Unchecked += (_, _) => ToggleProjected(captured, false);

                var text = new TextBlock
                {
                    Text = captured.Text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = (Brush)FindResource(captured.Completed ? "TextMuted" : "TextPrimary"),
                    TextDecorations = captured.Completed ? TextDecorations.Strikethrough : null,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var row = new Grid { Margin = new Thickness(0, 1, 0, 1), ToolTip = captured.Completed ? "已完成（点掉恢复）" : null };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(check);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                list.Children.Add(row);
            }

            if (column.Notes.Count > 50)
            {
                list.Children.Add(Hint($"还有 {column.Notes.Count - 50} 条，去桌面上看"));
            }
        }

        void AddDraft()
        {
            var column = columns.FirstOrDefault(c => string.Equals(c.Id, item.Get("columnId"), StringComparison.Ordinal));
            if (column is null)
            {
                return;
            }

            if (!BoardEditor.TryAddNote(column, draft.Text, out _))
            {
                draft.Text = string.Empty;
                return;
            }

            draft.Text = string.Empty;
            PushUndo();
            Save();
            Refresh(force: true);
        }

        draft.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            AddDraft();
        };

        Refresh(force: true);
        _todoRefreshes[item.Id] = () => Refresh();

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
            Refresh(force: true);
        };

        // 旧版待办卡的正文：留着当备注（新卡片不写这个字段）
        var links = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        FillLinks(links, item.Get("text"));

        Grid.SetRow(comboHost, 0);
        Grid.SetRow(listScroll, 1);
        Grid.SetRow(draft, 2);
        Grid.SetRow(preview, 3);
        Grid.SetRow(links, 4);
        host.Children.Add(comboHost);
        host.Children.Add(listScroll);
        host.Children.Add(draft);
        if (!string.IsNullOrWhiteSpace(item.Get("text")))
        {
            host.Children.Add(preview);
            host.Children.Add(links);
        }
    }

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = (Brush)FindResource("TextMuted"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
    };

    /// <summary>卡内勾选：写回同一份 NoteColumn（投影是双向的）。</summary>
    private void ToggleProjected(Note note, bool completed)
    {
        if (note.Completed == completed)
        {
            return;
        }

        PushUndo();
        BoardEditor.SetCompleted(note, completed);
        Save();
        foreach (var refresh in _todoRefreshes.Values)
        {
            refresh();
        }
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
                Source = LoadBitmap(path, item.W),
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
        var plan = mode == TimerMode.Pomodoro ? PomodoroPlan.Parse(item.Get("plan")) : null;
        if (plan is not null)
        {
            duration = plan.TotalSeconds();
        }

        var state = TimerStates.Infer(item.Get("state"), item.Get("startedAt"), BaseElapsed(item), item.Get("fired"));
        bool running = state == TimerState.Running;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = CanvasClock.ModeLabel(mode),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMuted"),
        });
        header.Children.Add(new TextBlock
        {
            Text = " · " + TimerStates.Label(state),
            FontSize = 11,
            Foreground = (Brush)FindResource(state == TimerState.Done ? "Accent" : "TextMuted"),
        });
        host.Children.Add(header);

        var display = new TextBlock
        {
            Text = PlanDisplayText(item, mode, duration, plan),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _timerTexts[item.Id] = display;
        Grid.SetRow(display, 1);
        host.Children.Add(display);

        if (plan is not null)
        {
            var step = plan.At(TotalElapsed(item));
            var segment = new TextBlock
            {
                Text = step.Finished ? "全部走完" : step.Label,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            Grid.SetRow(segment, 1);
            host.Children.Add(segment);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var start = new Button
        {
            Content = TimerStates.ActionLabel(state),
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            ToolTip = running ? "暂停" : TimerStates.ActionLabel(state),
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
        // 「重新开始」= 从 0 走一遍（plan §5.5 的状态机里 Done 只能回 Running，不能接着跑）
        if (string.Equals(item.Get("state"), TimerStates.Done, StringComparison.OrdinalIgnoreCase))
        {
            item.Payload["elapsed"] = "0";
        }

        item.Payload["startedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        item.Payload["fired"] = string.Empty;
        item.Payload["state"] = TimerStates.Running;
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
        item.Payload["state"] = TimerStates.Paused;
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
        item.Payload["state"] = TimerStates.Idle;
        Save();
        Rebuild();
    }

    private int TotalElapsed(CanvasItem item)
        => BaseElapsed(item) + CanvasClock.ElapsedSeconds(item.Get("startedAt"), DateTimeOffset.Now);

    /// <summary>卡片中央的数字：番茄钟按整份计划算总剩余，其它沿用 CanvasClock。</summary>
    private string PlanDisplayText(CanvasItem item, TimerMode mode, int duration, PomodoroPlan? plan)
    {
        if (plan is not null)
        {
            return CanvasClock.Format(Math.Max(0, plan.TotalSeconds() - TotalElapsed(item)));
        }

        return CanvasClock.Format(
            CanvasClock.DisplaySeconds(mode, duration, item.Get("startedAt"), BaseElapsed(item), DateTimeOffset.Now));
    }

    private void ReconfigureTimer(CanvasItem item)
    {
        var mode = CanvasClock.ParseMode(item.Get("mode"));
        int seconds = int.TryParse(item.Get("seconds"), out int stored) ? stored : CanvasClock.DefaultSeconds(mode);
        var plan = PomodoroPlan.Parse(item.Get("plan"));
        var dialog = new TimerSetupDialog(mode, seconds, plan) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        item.Payload["mode"] = CanvasClock.ModeKey(dialog.Mode);
        item.Payload["seconds"] = dialog.Seconds.ToString(CultureInfo.InvariantCulture);
        item.Payload["plan"] = dialog.Plan.ToKey();
        item.Payload["startedAt"] = string.Empty;
        item.Payload["fired"] = string.Empty;
        item.Payload["elapsed"] = "0";
        // 配置完停在 Configured，绝不自动开始（plan §5.5）
        item.Payload["state"] = TimerStates.Configured;
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
        // 桌面上增删条目后，画板上的栏目投影最多 1 s 跟上（plan §5.5 V-14）
        foreach (var refresh in _todoRefreshes.Values)
        {
            refresh();
        }

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
            var plan = mode == TimerMode.Pomodoro ? PomodoroPlan.Parse(item.Get("plan")) : null;
            if (_timerTexts.TryGetValue(item.Id, out var display))
            {
                display.Text = PlanDisplayText(item, mode, duration, plan);
            }

            bool finished = plan is not null
                ? plan.At(TotalElapsed(item)).Finished
                : CanvasClock.IsFinished(mode, duration, item.Get("startedAt"), BaseElapsed(item), now);
            if (string.IsNullOrWhiteSpace(item.Get("startedAt"))
                || string.Equals(item.Get("fired"), "1", StringComparison.Ordinal)
                || !finished)
            {
                continue;
            }

            item.Payload["fired"] = "1";
            item.Payload["startedAt"] = string.Empty;
            item.Payload["state"] = TimerStates.Done;
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

    private static BitmapImage LoadBitmap(string path, double displayWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        // 按显示尺寸解码：50 张图的显存增量才守得住 ≤8 MB（plan §4.8）
        var pixelWidth = (int)Math.Round(displayWidth);
        if (pixelWidth > 0)
        {
            image.DecodePixelWidth = pixelWidth;
        }

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

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        // 图片不属于结构化表面（§4.7）：把文件列表交给宿主，由它切到自由表面再落位
        var images = files.Where(ImageAssets.IsSupported).ToList();
        if (images.Count == 0)
        {
            return;
        }

        ImageDropRequested?.Invoke(this, images);
        e.Handled = true;
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
            return;
        }

        // F2 改名：和双击标题等价（plan §5.5）；输入框里不抢
        if (e.Key == Key.F2 && _selected is not null && !InTextBox(e.OriginalSource))
        {
            BeginRenameSelected();
            e.Handled = true;
        }
    }

    private bool TryPaste()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                // 结构化表面不产图片卡（§4.7）；交给宿主切到自由表面再粘贴
                ImagePasteRequested?.Invoke(this, EventArgs.Empty);
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

        // 按下先不拖：单击只选中，移动超过阈值才算拖动（plan §5.5 拖动与改名分离）。
        // 这样双击改名的第一下也不会把卡片挪走几个像素。
        _dragCard = card;
        _dragOrigin = e.GetPosition(this);
        _startX = item.X;
        _startY = item.Y;
        _dragStarted = false;
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

        var position = e.GetPosition(this);
        var delta = position - _dragOrigin;
        if (!_dragStarted)
        {
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            {
                return;
            }

            _dragStarted = true;
            PushUndo();
            _dragCard.CaptureMouse();
        }

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

        if (_dragStarted)
        {
            _dragCard.ReleaseMouseCapture();
            Save();
            e.Handled = true;
        }

        _dragCard = null;
        _dragStarted = false;
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

    private string Snapshot() => CanvasSnapshot.Capture(_document, Surface);

    private void Restore(string json)
    {
        // 只换回本表面的卡片：自由表面的图元不能被结构化表面的 Ctrl+Z 动到（V-13）。
        CanvasSnapshot.Restore(_document, Surface, json);
        _selected = _document.Items.FirstOrDefault(item =>
            string.Equals(item.Id, _selected?.Id, StringComparison.OrdinalIgnoreCase));
        Rebuild();
    }

    private void PushUndo() => _undo.Push(Surface, Snapshot());

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
