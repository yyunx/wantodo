using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Wantodo.App.Interop;
using Wantodo.App.Storage;
using Wantodo.Core;

namespace Wantodo.App.Windows;

/// <summary>形态 B 的独立窗口：工具条 + 自由画布。只在 <c>PanelMode=Canvas</c> 时由顶边服务唤起。</summary>
public partial class CanvasPanelWindow : Window
{
    private readonly CanvasStore _store;

    public CanvasPanelWindow()
    {
        InitializeComponent();

        _store = new CanvasStore(DataRootService.Paths.CanvasFile);
        Board.Attach(_store.Load(), _store);
        Board.StatusChanged += (_, _) => UpdateStatus();
        UpdateStatus();

        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => DesktopLayer.TryRoundCorners(new WindowInteropHelper(this).Handle);
    }

    public void ShowCanvas()
    {
        Show();
        Activate();
        Board.Focus();
    }

    private void UpdateStatus() => StatusText.Text = $"{Board.Document.Items.Count} 张 · Ctrl+Z 撤销";

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标已经抬起时 DragMove 会抛，忽略即可
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Board.Undo();
            UpdateStatus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Board.Redo();
            UpdateStatus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            Board.DeleteSelected();
            UpdateStatus();
            e.Handled = true;
        }
    }

    private void OnAddNote(object sender, RoutedEventArgs e) => AddAtViewportCenter("note");

    private void OnAddTodo(object sender, RoutedEventArgs e) => AddAtViewportCenter("todo");

    private void OnAddTimer(object sender, RoutedEventArgs e) => AddAtViewportCenter("timer");

    private void OnAddLink(object sender, RoutedEventArgs e) => AddAtViewportCenter("link");

    private void OnAddImage(object sender, RoutedEventArgs e) => AddAtViewportCenter("image");

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void AddAtViewportCenter(string kind)
    {
        var centerX = Scroller.HorizontalOffset + Math.Max(0, Scroller.ViewportWidth / 2);
        var centerY = Scroller.VerticalOffset + Math.Max(0, Scroller.ViewportHeight / 2);
        Board.Add(kind, centerX, centerY);
        UpdateStatus();
    }
}
