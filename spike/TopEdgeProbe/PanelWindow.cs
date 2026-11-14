using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TopEdgeProbe.Interop;

namespace TopEdgeProbe;

/// <summary>
/// P4：ShowActivated=false 的面板，对比三种收回判定——Deactivated / 鼠标捕获 / 全局钩子点外。
/// 探针只记录事件，是否算「误收回」由用户按计数按钮标注。
/// </summary>
internal sealed class PanelWindow : Window
{
    private readonly TextBlock _hint;
    private readonly Border _root;

    public PanelWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 520;
        Height = 340;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = 8;

        _hint = new TextBlock
        {
            Text = "面板测试区：在里面连续点 100 次，看会不会自己收回去",
            Foreground = Brushes.White,
            Margin = new Thickness(16),
            TextWrapping = TextWrapping.Wrap,
        };

        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x81, 0xF7)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = new StackPanel { Children = { _hint } },
        };

        Content = _root;

        Deactivated += (_, _) => Log("deactivated");
        LostFocus += (_, _) => Log("lost-focus");
        LostMouseCapture += (_, _) => Log("lost-mouse-capture");
        IsVisibleChanged += (_, _) => ProbeLog.Write("panel", "visible=" + IsVisible);
    }

    public string Mode { get; set; } = "hook";

    public int Opens { get; private set; }

    public void ShowProbe()
    {
        Opens++;
        Show();
        Activate();
        ProbeLog.Write("panel", $"open #{Opens} mode={Mode}");

        if (Mode == "capture")
        {
            var ok = Mouse.Capture(this);
            ProbeLog.Write("panel", "capture=" + ok);
        }
    }

    public void Retract(string why)
    {
        if (!IsVisible)
        {
            return;
        }

        ProbeLog.Write("panel", "retract:" + why);
        Mouse.Capture(null);
        Hide();
    }

    public bool ContainsScreenPoint(int x, int y)
    {
        if (!IsVisible)
        {
            return false;
        }

        // 钩子给的是物理像素；PointToScreen 与 ActualWidth 是 DIP，混用会让右侧一部分被判成「面板外」。
        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            return false;
        }

        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }

    public string DescribeClick(int x, int y)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            return $"click=({x},{y}) rect=?";
        }

        return $"click=({x},{y}) rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})";
    }

    private void Log(string what)
    {
        ProbeLog.Write("panel", what + (IsVisible ? " (visible)" : " (hidden)"));
    }
}
