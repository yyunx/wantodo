using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopLayerProbe.Interop;
using static DesktopLayerProbe.Interop.NativeMethods;

namespace DesktopLayerProbe;

/// <summary>
/// 一次性探针窗口。允许丑，允许硬编码。
/// 只回答一个问题：这块窗口能不能稳定挂在桌面图标层，并且 Win+D / Explorer 重启后还在。
/// </summary>
internal sealed class ProbeWindow : Window
{
    private const int HotKeyFirst = 1;
    private const int HotKeyLast = 5;
    private const int HotKeyDump = 20;
    private const int HotKeyReshow = 21;
    private const int HotKeyQuit = 22;

    private readonly TextBlock _headline;
    private readonly TextBlock _body;
    private readonly TextBlock _footer;

    private DesktopLayerService? _service;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private uint _taskbarCreatedMessage;
    private bool _autoReshow = true;
    private int _hideEvents;
    private int _reshowAttempts;
    private int _reattachCount;

    public ProbeWindow()
    {
        Title = "wantodo desktop-layer probe";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = false;   // 硬规则：常驻小组件绝不走分层窗口
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 160;
        Top = 160;
        Width = 860;
        Height = 340;
        Background = new SolidColorBrush(Color.FromRgb(0x11, 0x15, 0x1B));

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

        _headline = new TextBlock
        {
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Text = "wantodo · desktop layer probe",
        };

        _body = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xC4)),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
        };

        _footer = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x70, 0x84)),
            Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "Ctrl+Alt+1..5 切换层级   Ctrl+Alt+D 诊断到日志   Ctrl+Alt+R 自动抢回开关   Ctrl+Alt+Q 退出   拖动窗口 = 移动",
        };

        panel.Children.Add(_headline);
        panel.Children.Add(_body);
        panel.Children.Add(_footer);
        Content = panel;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // 鼠标已松开，忽略
                }
            }
        };

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WindowProcedure);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        ProbeLog.Section("启动");
        ProbeLog.Info($"探针窗口 hwnd={Hex(_hwnd)}");
        RegisterHotKeys();

        _service = new DesktopLayerService(_hwnd);
        ProbeLog.Section("拓扑（催生 WorkerW 之前）");
        ProbeLog.Write("TOPO", _service.Topology.Summary);
        ProbeLog.Block("TOPO", _service.Topology.Detail);

        _service.TrySpawnWorkerW();
        _service.RefreshTopology();

        ApplyMode(LayerMode.WorkerWOwner, "startup");
        _service.Show();
        UpdateText();
    }

    private void RegisterHotKeys()
    {
        var modifiers = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;

        for (var id = HotKeyFirst; id <= HotKeyLast; id++)
        {
            var vk = (uint)('0' + id);
            if (!RegisterHotKey(_hwnd, id, modifiers, vk))
            {
                ProbeLog.Warn($"RegisterHotKey(Ctrl+Alt+{id}) 失败，可能被其他程序占用");
            }
        }

        TryRegister(HotKeyDump, 'D');
        TryRegister(HotKeyReshow, 'R');
        TryRegister(HotKeyQuit, 'Q');

        void TryRegister(int id, char key)
        {
            if (!RegisterHotKey(_hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, key))
            {
                ProbeLog.Warn($"RegisterHotKey(Ctrl+Alt+{key}) 失败");
            }
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WM_HOTKEY)
        {
            OnHotKey(wParam.ToInt32());
            handled = true;
            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && message == (int)_taskbarCreatedMessage)
        {
            ProbeLog.Section("收到 TaskbarCreated —— Explorer 重启");
            ScheduleReattach();
            handled = true;
            return IntPtr.Zero;
        }

        if (message == WM_SHOWWINDOW && wParam == IntPtr.Zero)
        {
            _hideEvents++;
            ProbeLog.Write("HIDE", $"WM_SHOWWINDOW(FALSE)，第 {_hideEvents} 次；mode={_service?.Mode}");
            if (_autoReshow)
            {
                ScheduleReshow();
            }

            UpdateText();
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void OnHotKey(int id)
    {
        switch (id)
        {
            case HotKeyFirst + 0:
                ApplyMode(LayerMode.WorkerWOwner, "hotkey 1");
                break;
            case HotKeyFirst + 1:
                ApplyMode(LayerMode.ProgmanOwner, "hotkey 2");
                break;
            case HotKeyFirst + 2:
                ApplyMode(LayerMode.Topmost, "hotkey 3");
                break;
            case HotKeyFirst + 3:
                ApplyMode(LayerMode.Normal, "hotkey 4");
                break;
            case HotKeyFirst + 4:
                ApplyMode(LayerMode.WorkerWOwnerBottom, "hotkey 5");
                break;
            case HotKeyDump:
                DumpDiagnostics("hotkey D");
                break;
            case HotKeyReshow:
                _autoReshow = !_autoReshow;
                ProbeLog.Info($"自动抢回显示 = {_autoReshow}");
                UpdateText();
                break;
            case HotKeyQuit:
                ProbeLog.Info("用户退出探针");
                Close();
                break;
        }
    }

    private void ApplyMode(LayerMode mode, string reason)
    {
        if (_service is null)
        {
            return;
        }

        // 让 WPF 自己的 Topmost 状态与原生一致，避免两条路径互相打架
        Topmost = mode == LayerMode.Topmost;
        _service.Apply(mode, reason);
        _service.Show();
        UpdateText();
    }

    private void DumpDiagnostics(string reason)
    {
        if (_service is null)
        {
            return;
        }

        ProbeLog.Section($"诊断 dump（{reason}）");
        _service.RefreshTopology();
        ProbeLog.Block("STATE", _service.DescribeState());
        ProbeLog.Info($"hideEvents={_hideEvents}, reshowAttempts={_reshowAttempts}, reattach={_reattachCount}");
    }

    /// <summary>Shell 想把我们藏起来时，稍后自己抢回来，并记录每次尝试。</summary>
    private void ScheduleReshow()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _reshowAttempts++;
            _service?.Show();
            ProbeLog.Write("RESHOW", $"第 {_reshowAttempts} 次抢回；visible={IsWindowVisible(_hwnd)}");
            UpdateText();
        };
        timer.Start();
    }

    /// <summary>Explorer 重启后 WorkerW 是新的句柄，必须重新取拓扑再挂一次。</summary>
    private void ScheduleReattach()
    {
        var delays = new[] { 300, 1500, 4000 };
        foreach (var delay in delays)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _reattachCount++;
                var mode = _service?.Mode ?? LayerMode.WorkerWOwner;
                ProbeLog.Write("REATTACH", $"第 {_reattachCount} 次，delay={delay}ms，mode={mode}");
                _service?.RefreshTopology();
                ApplyMode(mode, $"reattach after TaskbarCreated ({delay}ms)");
            };
            timer.Start();
        }
    }

    private void UpdateText()
    {
        if (_service is null)
        {
            return;
        }

        _headline.Text = $"{_service.Mode}：{_service.Mode.Describe()}";

        var lines = new[]
        {
            "Ctrl+Alt+1  WorkerW owner（目标形态）",
            "Ctrl+Alt+2  Progman owner",
            "Ctrl+Alt+3  Topmost（对照）",
            "Ctrl+Alt+4  Normal（对照）",
            "Ctrl+Alt+5  WorkerW owner + HWND_BOTTOM",
            string.Empty,
            "拓扑：" + _service.TopologySummary,
            string.Empty,
            $"隐藏事件 {_hideEvents} 次 · 抢回 {_reshowAttempts} 次 · 重挂 {_reattachCount} 次 · 自动抢回={_autoReshow}",
            "日志：%TEMP%\\wantodo-probe.log",
        };

        _body.Text = string.Join(Environment.NewLine, lines);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hwnd != IntPtr.Zero)
        {
            for (var id = HotKeyFirst; id <= HotKeyLast; id++)
            {
                _ = UnregisterHotKey(_hwnd, id);
            }

            _ = UnregisterHotKey(_hwnd, HotKeyDump);
            _ = UnregisterHotKey(_hwnd, HotKeyReshow);
            _ = UnregisterHotKey(_hwnd, HotKeyQuit);
        }

        _source?.RemoveHook(WindowProcedure);
        ProbeLog.Info("探针窗口关闭");
    }
}
