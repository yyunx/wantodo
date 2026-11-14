using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TopEdgeProbe.Interop;

namespace TopEdgeProbe;

/// <summary>
/// P2 的「无边框全屏」对照组：铺满主屏、无边框，用来验证前台窗口覆盖判定；
/// 独占全屏需要真实游戏/视频，探针造不出来，只能由用户跑。
/// </summary>
internal sealed class FullscreenTestWindow : Window
{
    public FullscreenTestWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));

        Content = new TextBlock
        {
            Text = "无边框全屏测试窗 · 按 Esc 关闭",
            Foreground = Brushes.White,
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        Loaded += (_, _) =>
        {
            Activate();
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            ProbeLog.Info($"fullscreen-test-window: hwnd=0x{handle.ToInt64():X} size={Width}x{Height} dpi={NativeMethods.GetDpiForWindow(handle)}");
        };

        Closed += (_, _) => ProbeLog.Info("fullscreen-test-window: closed");
    }
}
