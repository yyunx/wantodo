using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TopEdgeProbe.Interop;

namespace TopEdgeProbe;

/// <summary>
/// P1 的第一条路：贴在主屏顶边的 2 DIP 窗口，靠 MouseEnter 计数。
/// 不设 AllowsTransparency（产品硬规则），2 DIP 的实心条会被用户看见——这正是要测的遮挡。
/// </summary>
internal sealed class HotZoneWindow : Window
{
    private readonly Border _border;
    private int _enters;

    public HotZoneWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = 2;

        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7)),
        };

        Content = _border;
        _border.MouseEnter += OnEnter;
        _border.MouseLeave += OnLeave;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(style));
            ProbeLog.Info($"hotzone-window: hwnd=0x{handle.ToInt64():X} heightDip={Height} widthDip={Width} dpi={NativeMethods.GetDpiForWindow(handle)}");
        };
    }

    public event Action<int>? Entered;

    public int Enters => _enters;

    public void Reset() => _enters = 0;

    private void OnEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _enters++;
        Entered?.Invoke(_enters);

        if (_enters <= 5 || _enters % 10 == 0)
        {
            ProbeLog.Info($"hotzone-enter #{_enters}");
        }
    }

    private void OnLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ProbeLog.Write("hotzone-leave", "#" + _enters);
    }
}
