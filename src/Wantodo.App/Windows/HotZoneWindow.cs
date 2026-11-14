using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wantodo.App.Interop;

namespace Wantodo.App.Windows;

/// <summary>
/// 主屏顶边 2 DIP 的透明热区。Phase 16 实测：窗口热区 50/50，比低级钩子稳。
/// 不抢焦点、不进任务栏；鼠标进入才开始 120 ms 停留计时。
/// </summary>
internal sealed class HotZoneWindow : Window
{
    public HotZoneWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = 2;

        // 不能真透明（全透明像素不参与命中测试），用 1% 不透明度：肉眼看不见，鼠标能撞上。
        Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7));
        Opacity = 0.01;

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private void ApplyExStyles()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        long style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }
}
