using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Wantodo.Core;

namespace Wantodo.App.Controls;

/// <summary>
/// 形态 B 的自由画布：卡片是绝对定位的 Border，拖动与缩放都在这层处理。
/// 数据仍由 <see cref="CanvasDocument"/> 持有，写盘走 <see cref="CanvasStore"/>。
/// </summary>
public sealed class CanvasBoard : Canvas
{
    private const double GridStep = 8;
    private const int UndoLimit = 20;

    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly Dictionary<string, Border> _cards = new(StringComparer.OrdinalIgnoreCase);

    private CanvasDocument _document = new();
    private CanvasStore? _store;
    private CanvasItem? _selected;

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
    }

    public CanvasDocument Document => _document;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? StatusChanged;

    public void Attach(CanvasDocument document, CanvasStore store)
    {
        _document = document ?? new CanvasDocument();
        _store = store;
        Rebuild();
    }

    public void Rebuild()
    {
        Children.Clear();
        _cards.Clear();
        foreach (var item in _document.Items.OrderBy(i => i.ZIndex))
        {
            var card = BuildCard(item);
            Children.Add(card);
            _cards[item.Id] = card;
            Select(item, item.Id == _selected?.Id, rebuild: false);
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>在 <paramref name="centerX"/> 附近插入一张新卡片。</summary>
    public CanvasItem? Add(string kind, double centerX, double centerY)
    {
        PushUndo();
        var item = _document.Add(kind, Math.Max(0, centerX - 110), Math.Max(0, centerY - 80));
        item.Payload[kind switch
        {
            "todo" => "columnId",
            "timer" => "seconds",
            "link" => "url",
            "image" => "path",
            _ => "noteId",
        }] = kind switch
        {
            "timer" => "1500",
            _ => "",
        };

        var card = BuildCard(item);
        Children.Add(card);
        _cards[item.Id] = card;
        _selected = item;
        Select(item, true, rebuild: false);
        card.BringIntoView();
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

    private Border BuildCard(CanvasItem item)
    {
        var card = new Border
        {
            Width = item.W,
            Height = item.H,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("Divider"),
            Background = (Brush)FindResource("CardBackground"),
            Tag = item,
            SnapsToDevicePixels = true,
        };

        Canvas.SetLeft(card, item.X);
        Canvas.SetTop(card, item.Y);
        Panel.SetZIndex(card, item.ZIndex);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = KindLabel(item.Kind),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondary"),
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
        TextBlock? placeholder = null;
        if (string.Equals(item.Kind, "note", StringComparison.OrdinalIgnoreCase))
        {
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
            editor.TextChanged += (s, _) =>
            {
                var box = (TextBox)s!;
                item.Payload["text"] = box.Text;
                if (placeholder is not null)
                {
                    placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            editor.GotKeyboardFocus += (_, _) => PushUndo();
            editor.LostKeyboardFocus += (_, _) => Save();

            placeholder = new TextBlock
            {
                Text = "（空）",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextMuted"),
                IsHitTestVisible = false,
                Visibility = editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
            };
            bodyHost.Children.Add(editor);
            bodyHost.Children.Add(placeholder);
        }
        else
        {
            bodyHost.Children.Add(new TextBlock
            {
                Text = BodyText(item),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextPrimary"),
            });
        }

        Grid.SetRow(bodyHost, 1);

        // 缩放把：透明命中区 14×14，里面画三条斜线当图标；只有悬停时才亮，
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

    private static string BodyText(CanvasItem item)
    {
        var text = item.Kind switch
        {
            "timer" => int.TryParse(item.Get("seconds"), out var seconds) && seconds > 0
                ? $"{seconds / 60:00}:{seconds % 60:00}"
                : "25:00",
            "link" => item.Get("url") is { Length: > 0 } url ? url : "（未设置链接）",
            "image" => item.Get("path") is { Length: > 0 } path ? path : "（未选择图片）",
            "todo" => item.Get("columnId") is { Length: > 0 } column ? $"栏：{column}" : "（未选择栏）",
            _ => item.Get("noteId") is { Length: > 0 } note ? $"随笔：{note}" : "（空）",
        };

        return text;
    }

    private void Select(CanvasItem item, bool selected, bool rebuild)
    {
        if (!_cards.TryGetValue(item.Id, out var card))
        {
            return;
        }

        card.BorderBrush = (Brush)FindResource(selected ? "Accent" : "Divider");
        card.BorderThickness = new Thickness(selected ? 2 : 1);
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
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Save(_document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"画布保存失败：{ex.GetType().Name}: {ex.Message}");
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
