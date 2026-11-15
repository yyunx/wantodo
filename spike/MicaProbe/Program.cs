using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MicaProbe;

/// <summary>
/// 一次性探针（0.7.0 P0.5）：验证「禁止 AllowsTransparency 时 Mica 能不能透出来」。
/// 三种模式：
///   normal  —— 普通置顶窗口（相当于设置页）
///   desktop —— owner=WorkerW + WS_EX_NOACTIVATE（相当于贴桌面层的面板）
///   collapsed—— 先把窗口压到 12 DIP 再展开，看材质切换是否闪
/// 结果只写日志；截图由外部 pwsh 脚本按日志里的窗口矩形抓。
/// </summary>
public static class Program
{
    private static readonly List<string> Log = new();
    private static bool _chrome;
    private static bool _bgNull;

    [STAThread]
    public static int Main(string[] args)
    {
        var mode = Arg(args, "--mode", "normal");
        var material = Arg(args, "--material", "mica");
        _chrome = args.Contains("--chrome");
        _bgNull = args.Contains("--bgnull");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = BuildWindow(mode);
        var button = new Button { Content = "点我", Width = 120, Height = 34, Margin = new Thickness(24, 0, 0, 0) };
        button.Click += (_, _) => Line("[输入] 按钮点击生效 ✓");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "MicaProbe\n第二行用来对比材质是否透上来", Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(24, 24, 24, 12) });
        panel.Children.Add(button);
        ((Border)window.Content).Child = panel;

        // backdrop 模式：先在同一个位置铺一块高饱和洋红，用来分辨「真透出背后窗口」还是「Mica 只采样壁纸」
        Window? backdrop = null;
        if (mode == "backdrop")
        {
            backdrop = new Window
            {
                Left = window.Left,
                Top = window.Top,
                Width = window.Width,
                Height = window.Height,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = Brushes.Magenta,
                Content = null,
            };
            backdrop.Show();
        }

        window.SourceInitialized += (_, _) => Attach(window, mode, material);
        window.ContentRendered += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            LogWindow(hwnd, mode, material);

            if (mode == "collapsed")
            {
                PumpFor(1200);
                var sw = Stopwatch.StartNew();
                window.Height = 12;
                PumpFor(1500);
                LogRect(hwnd, "collapsed");
                window.Height = 320;
                PumpFor(900);
                sw.Stop();
                Line($"collapse+expand 总耗时 {sw.ElapsedMilliseconds} ms");
                LogRect(hwnd, "expanded-again");
            }

            if (mode == "click")
            {
                // 验证 GlassFrameThickness=-1 会不会把整窗变成 HTCAPTION 导致点不到控件
                PumpFor(500);
                var point = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                Native.SetCursorPos((int)point.X, (int)point.Y);
                PumpFor(120);
                Native.MouseLeftClick();
                PumpFor(600);
                Line($"[输入] 已在 {point.X:0},{point.Y:0} 模拟点击（若上面没有「按钮点击生效」，说明玻璃区吞了鼠标）");
                LogRect(hwnd, "after-click");
            }

            PumpFor(1500);
            app.Shutdown();
        };

        if (File.Exists(LogPath()))
        {
            File.Delete(LogPath());
        }

        Line($"=== 探针启动 mode={mode} material={material} ===");
        app.Run(window);
        return 0;
    }

    private static Window BuildWindow(string mode)
    {
        var window = new Window
        {
            Title = "MicaProbe " + mode,
            Width = 760,
            Height = 320,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            ShowInTaskbar = false,
            // 关键 1：客户端背景必须透明，不然 DWM 采样被像素盖住
            Background = Brushes.Transparent,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC4, 0x1A, 0x1B, 0x26)), // α0.77 的内容层
                CornerRadius = new CornerRadius(12),
                Child = new TextBlock
                {
                    Text = "MicaProbe\n第二行用来对比材质是否透上来",
                    Foreground = Brushes.White,
                    FontSize = 18,
                    Margin = new Thickness(24),
                },
            },
        };

        var area = SystemParameters.WorkArea;
        window.Left = area.Left + (area.Width - window.Width) / 2;
        window.Top = area.Top + 40;

        if (_chrome)
        {
            // 对照组 2：GlassFrameThickness=-1 让 DWM 接管边框，看材质能不能从客户区透出
            System.Windows.Shell.WindowChrome.SetWindowChrome(
                window,
                new System.Windows.Shell.WindowChrome
                {
                    GlassFrameThickness = new Thickness(-1),
                    ResizeBorderThickness = new Thickness(0),
                    CaptionHeight = 0,
                    CornerRadius = new CornerRadius(0),
                    UseAeroCaptionButtons = false,
                });
            window.Background = null;
        }

        if (_bgNull)
        {
            // 对照组 3：不装 WindowChrome，只把 Window.Background 设为 null
            window.Background = null;
        }

        return window;
    }

    private static void Attach(Window window, string mode, string material)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Line($"OS build={Environment.OSVersion.Version} ({(IsWindows11 ? "Win11" : "Win10/older")}) mode={mode} material={material}");

        if (mode == "desktop")
        {
            var workerW = Native.FindDesktopWorker();
            Line(workerW == IntPtr.Zero ? "desktop: 没找到 WorkerW，退化成普通窗口" : $"desktop: owner=WorkerW 0x{workerW:X}");
            if (workerW != IntPtr.Zero)
            {
                Native.SetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT, workerW);
                var ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE));
            }
        }

        // 关键 2：把非客户区扩到整个客户区（WPF 不许 AllowsTransparency 时的常规做法）
        var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var hrExtend = Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        Line($"DwmExtendFrameIntoClientArea hr=0x{hrExtend:X8}");

        if (!string.Equals(material, "none", StringComparison.OrdinalIgnoreCase))
        {
            var hr = TryBackdrop(hwnd, out var api);
            Line($"material: {api} hr=0x{hr:X8} → {(hr == 0 ? "已请求" : "失败（将走纯色降级）")}");
        }
        else
        {
            Line("material: none（对照组，纯透明窗口）");
        }

        var ok = Native.GetWindowRect(hwnd, out var rect);
        Line($"GetWindowRect ok={ok} rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} hwnd=0x{hwnd:X}");
    }

    private static int TryBackdrop(IntPtr hwnd, out string api)
    {
        var build = Environment.OSVersion.Version.Build;
        if (build >= 22621)
        {
            api = "DWMWA_SYSTEMBACKDROP_TYPE=DWMSBT_MAINWINDOW";
            var value = 2;
            return Native.DwmSetWindowAttribute(hwnd, 38, ref value, sizeof(int));
        }

        if (build >= 22000)
        {
            api = "DWMWA_MICA_EFFECT(1029)=1";
            var value = 1;
            return Native.DwmSetWindowAttribute(hwnd, 1029, ref value, sizeof(int));
        }

        api = "SetWindowCompositionAttribute ACCENT_ENABLE_ACRYLICBLURBEHIND";
        var accent = new Native.ACCENT_POLICY
        {
            AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
            GradientColor = unchecked((int)0xCC1A1B26),
        };
        var data = new Native.WINDOWCOMPOSITIONATTRIBDATA
        {
            Attribute = 19, // WCA_ACCENT_POLICY
            Data = Marshal.AllocHGlobal(Marshal.SizeOf<Native.ACCENT_POLICY>()),
            SizeOfData = Marshal.SizeOf<Native.ACCENT_POLICY>(),
        };
        try
        {
            Marshal.StructureToPtr(accent, data.Data, false);
            return Native.SetWindowCompositionAttribute(hwnd, ref data) ? 0 : Marshal.GetLastWin32Error();
        }
        catch (DllNotFoundException)
        {
            return unchecked((int)0x80004005);
        }
        catch (EntryPointNotFoundException)
        {
            return unchecked((int)0x80004005);
        }
        finally
        {
            Marshal.FreeHGlobal(data.Data);
        }
    }

    private static void LogWindow(IntPtr hwnd, string mode, string material)
    {
        Line($"内容已渲染：mode={mode} material={material} hwnd=0x{hwnd:X}");
        LogRect(hwnd, "rendered");
    }

    private static void LogRect(IntPtr hwnd, string tag)
    {
        Native.GetWindowRect(hwnd, out var rect);
        Line($"{tag}: rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} size={rect.Right - rect.Left}x{rect.Bottom - rect.Top}");
    }

    private static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    private static string Arg(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static void PumpFor(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Thread.Sleep(16);
        }
    }

    // 逐行落盘：外部 pwsh 要在进程还活着时读窗口矩形来截屏
    private static void Line(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {text}";
        Log.Add(line);
        File.AppendAllText(LogPath(), line + Environment.NewLine);
    }

    private static string LogPath()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "verify", "0.7.0");
        Directory.CreateDirectory(dir);
        return Path.GetFullPath(Path.Combine(dir, "probe-mica.log"));
    }
}
