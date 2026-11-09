using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Wantodo.App.Interop;
using Wantodo.Core;

namespace Wantodo.App;

public partial class MainWindow : Window
{
    private readonly Board _board;
    private readonly JsonBoardStore _store;

    private IntPtr _hwnd;
    private ShellRestartWatcher? _shellRestartWatcher;

    public MainWindow(Board board, JsonBoardStore store)
    {
        _board = board;
        _store = store;

        InitializeComponent();
        DataContext = board;

        // 数据里一栏都没有时界面会是空的，给个最小可用的占位栏（不改用户的 JSON，只在内存里补）
        if (_board.Columns.Count == 0)
        {
            _board.Columns.Add(new NoteColumn { Title = "未命名栏" });
            AppLog.Warn("数据里没有栏，已临时补一栏");
        }

        ApplyPlacement();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        AppLog.Info($"窗口就绪 hwnd=0x{_hwnd.ToInt64():X} {Width}×{Height} DIP @ {Left},{Top}");

        DesktopLayer.TryRoundCorners(_hwnd);

        if (!DesktopLayer.Attach(_hwnd, _board.Surface))
        {
            AppLog.Warn($"没能挂到 {_board.Surface} 层；" + DesktopLayer.Describe(_hwnd));
        }

        if (string.Equals(_board.Surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase))
        {
            _shellRestartWatcher = ShellRestartWatcher.Install(_hwnd, OnShellRestarted);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _shellRestartWatcher?.Dispose();
        _shellRestartWatcher = null;

        SavePlacement();

        base.OnClosing(e);
    }

    private void OnShellRestarted()
    {
        var ok = DesktopLayer.Attach(_hwnd, _board.Surface);
        AppLog.Info($"Explorer 重启后重挂 {(ok ? "成功" : "失败")}；" + DesktopLayer.Describe(_hwnd));
    }

    private void ApplyPlacement()
    {
        var placement = _board.Placement;
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

    private void SavePlacement()
    {
        _board.Placement.Left = Left;
        _board.Placement.Top = Top;
        _board.Placement.Width = ActualWidth > 0 ? ActualWidth : Width;
        _board.Placement.Height = ActualHeight > 0 ? ActualHeight : Height;

        try
        {
            _store.Save(_board);
            AppLog.Info($"已保存布局 {_board.Placement.Width}×{_board.Placement.Height} @ {Left},{Top}");
        }
        catch (Exception ex)
        {
            AppLog.Error("保存布局失败", ex);
        }
    }

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
}
