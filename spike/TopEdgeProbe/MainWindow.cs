using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TopEdgeProbe.Interop;

namespace TopEdgeProbe;

internal sealed class MainWindow : Window
{
    private readonly HotZoneWindow _strip;
    private readonly PanelWindow _panel = new();
    private readonly MouseEdgeHook _hook = new();
    private readonly ForegroundWatcher _watcher = new();

    private readonly TextBlock _stripCount = new();
    private readonly TextBlock _hookCount = new();
    private readonly TextBlock _edgeHintCount = new();
    private readonly TextBlock _autoHideCount = new();
    private readonly TextBlock _dpiText = new();
    private readonly TextBlock _foregroundText = new();
    private readonly TextBlock _p4Text = new();

    private int _edgeHint;
    private int _autoHide;
    private int _falseRetract;
    private int _correctRetract;
    private int _dpi;

    public MainWindow()
    {
        Title = "wantodo TopEdgeProbe";
        Width = 760;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _strip = new HotZoneWindow();
        _strip.Entered += _ => Refresh();

        Content = BuildUi();

        Loaded += (_, _) =>
        {
            _strip.Show();

            var handle = new WindowInteropHelper(this).Handle;
            var dpi = (int)NativeMethods.GetDpiForWindow(handle);
            _hook.EdgeRow = Math.Max(1, (int)Math.Round(2 * dpi / 96.0));
            _hook.Start();
            _hook.EdgeTouched += (x, y) => Dispatcher.BeginInvoke(() => ProbeLog.Write("hook", $"edge-touch x={x} y={y}"));

            _hook.LeftButtonDown += OnGlobalClick;
            _watcher.Start();
            _watcher.Captured += message => Dispatcher.BeginInvoke(() => _foregroundText.Text = message);

            ProbeLog.Section("probe started");
            ProbeLog.Info($"main dpi={dpi} stripHeight=2dip hookEdgeRow={_hook.EdgeRow}px log={ProbeLog.Path}");
            Refresh();
        };

        Closing += (_, _) =>
        {
            _hook.Dispose();
            _watcher.Dispose();
            _strip.Close();
            _panel.Close();
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }

    private UIElement BuildUi()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = "日志：" + ProbeLog.Path,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Brushes.DimGray,
        });

        panel.Children.Add(Header("P1 · 顶边命中率（窗口热区 vs 低级鼠标钩子）"));
        panel.Children.Add(Text(
            "1. 点「清零」，2. 把鼠标从屏幕中间快速甩到屏幕顶边、再甩回来，重复 50 次。" +
            "3. 点「写下 P1 结论」。两个计数应各为 50；差多少就是漏判。"));
        panel.Children.Add(_stripCount);
        panel.Children.Add(_hookCount);
        panel.Children.Add(Row(
            Button("清零", (_, _) => { _strip.Reset(); _hook.Reset(); Refresh(); }),
            Button("写下 P1 结论", (_, _) => WriteP1())));

        panel.Children.Add(Header("P1 · 副作用计数（人工观察）"));
        panel.Children.Add(Text("最大化一个普通窗口后反复撞顶：出现贴边布局提示 / 窗口被贴边收窄，各按一次。"));
        panel.Children.Add(_edgeHintCount);
        panel.Children.Add(_autoHideCount);
        panel.Children.Add(Row(
            Button("贴边提示 +1", (_, _) => { _edgeHint++; Refresh(); }),
            Button("任务栏被触发 +1", (_, _) => { _autoHide++; Refresh(); }),
            Button("写下副作用", (_, _) => WriteSideEffects())));

        panel.Children.Add(Header("P3 · DPI"));
        panel.Children.Add(_dpiText);
        panel.Children.Add(Button("记录当前 DPI 与热区物理高度", (_, _) => LogDpi()));

        panel.Children.Add(Header("P2 · 全屏检测"));
        panel.Children.Add(Text("切到独占全屏与无边框全屏各一次；最后一行应出现 covers=True。"));
        panel.Children.Add(_foregroundText);
        panel.Children.Add(Row(
            Button("立即采样前台窗口", (_, _) => _watcher.CaptureNow("manual")),
            Button("开无边框全屏测试窗", (_, _) => new FullscreenTestWindow().Show())));

        panel.Children.Add(Header("P4 · 面板收回判定"));
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var mode in new[] { "lostfocus", "capture", "hook" })
        {
            var radio = new RadioButton
            {
                Content = mode,
                GroupName = "retract",
                Margin = new Thickness(0, 0, 16, 0),
                IsChecked = mode == "hook",
                Tag = mode,
            };
            radio.Checked += (_, _) => { _panel.Mode = mode; ProbeLog.Write("p4", "mode=" + mode); };
            modes.Children.Add(radio);
        }

        panel.Children.Add(modes);
        panel.Children.Add(Text("打开面板后，在里面连续点 100 次；自己收回一次就按「误收回 +1」。"));
        panel.Children.Add(_p4Text);
        panel.Children.Add(Row(
            Button("打开面板", (_, _) => _panel.ShowProbe()),
            Button("误收回 +1", (_, _) => { _falseRetract++; Refresh(); }),
            Button("正确收回 +1", (_, _) => { _correctRetract++; Refresh(); }),
            Button("写下 P4", (_, _) => WriteP4())));

        panel.Children.Add(Header("收尾"));
        panel.Children.Add(Button("打开日志所在目录", (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + ProbeLog.Path + "\"")));

        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void OnGlobalClick(int x, int y)
    {
        if (!_panel.IsVisible)
        {
            return;
        }

        if (_panel.Mode == "hook" && !_panel.ContainsScreenPoint(x, y))
        {
            ProbeLog.Write("p4", "click-outside " + _panel.DescribeClick(x, y));
            _panel.Retract("click-outside");
        }
    }

    private void Refresh()
    {
        _stripCount.Text = $"窗口热区命中：{_strip.Enters}";
        _hookCount.Text = $"钩子命中：{_hook.Hits}（顶边 {_hook.EdgeRow} 物理像素）";
        _edgeHintCount.Text = $"贴边提示出现：{_edgeHint}";
        _autoHideCount.Text = $"任务栏自动隐藏被触发：{_autoHide}";
        _p4Text.Text = $"打开次数 {_panel.Opens} · 误收回 {_falseRetract} · 正确收回 {_correctRetract} · 模式 {_panel.Mode}";
        _dpiText.Text = _dpi == 0 ? "尚未记录" : $"DPI {_dpi}（{_dpi / 96.0:P0}）· 热区物理高度 {Math.Round(2 * _dpi / 96.0)} px";
    }

    private void WriteP1()
    {
        ProbeLog.Section("P1 result");
        ProbeLog.Info($"swipes=50 hotzoneHits={_strip.Enters} hookHits={_hook.Hits} edgeHint={_edgeHint} taskbarAutoHideTriggers={_autoHide}");
    }

    private void WriteSideEffects()
    {
        ProbeLog.Section("P1 side effects");
        ProbeLog.Info($"edge-snap-prompts={_edgeHint} taskbar-auto-hide-triggers={_autoHide}");
    }

    private void LogDpi()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _dpi = (int)NativeMethods.GetDpiForWindow(handle);
        ProbeLog.Section("P3 dpi");
        ProbeLog.Info($"dpi={_dpi} scale={_dpi / 96.0:F2} stripDip=2 stripPx={Math.Round(2 * _dpi / 96.0)} hookEdgeRowPx={_hook.EdgeRow}");
        Refresh();
    }

    private void WriteP4()
    {
        ProbeLog.Section("P4 result");
        ProbeLog.Info($"mode={_panel.Mode} opens={_panel.Opens} falseRetract={_falseRetract} correctRetract={_correctRetract}");
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 6),
    };

    private static TextBlock Text(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.DimGray,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var child in children)
        {
            if (child is FrameworkElement element)
            {
                element.Margin = new Thickness(0, 0, 8, 0);
            }

            row.Children.Add(child);
        }

        return row;
    }

    private static Button Button(string text, RoutedEventHandler onClick)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4) };
        button.Click += onClick;
        return button;
    }
}
