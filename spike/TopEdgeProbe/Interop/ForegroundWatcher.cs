using System;
using System.Runtime.InteropServices;
using System.Text;
using TopEdgeProbe.Interop;

namespace TopEdgeProbe.Interop;

/// <summary>
/// P2：前台窗口变化时，记录窗口矩形、显示器矩形、是否覆盖整个显示器，
/// 以及任务栏是否开了自动隐藏。全屏应用由用户切到前台，探针只负责留证据。
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly NativeMethods.WinEventProc _proc;
    private IntPtr _hook;

    public ForegroundWatcher()
    {
        _proc = OnEvent;
    }

    public event Action<string>? Captured;

    public void Start()
    {
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _proc,
            0,
            0,
            NativeMethods.WineventOutofcontext);

        ProbeLog.Info("foreground-watcher: " + (_hook == IntPtr.Zero ? "安装失败" : "已安装"));
    }

    public void CaptureNow(string why) => Probe("foreground(" + why + ")");

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void OnEvent(
        IntPtr winEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType != NativeMethods.EventSystemForeground || hwnd == IntPtr.Zero)
        {
            return;
        }

        Probe("foreground", hwnd);
    }

    private void Probe(string tag, IntPtr hwnd = default)
    {
        if (hwnd == IntPtr.Zero)
        {
            hwnd = NativeMethods.GetForegroundWindow();
        }

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);

        var covers = rect.Left <= info.Monitor.Left
                     && rect.Top <= info.Monitor.Top
                     && rect.Right >= info.Monitor.Right
                     && rect.Bottom >= info.Monitor.Bottom;

        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        var autoHide = IsTaskbarAutoHide();
        var title = Title(hwnd);

        var message =
            $"{tag} title=\"{title}\" rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) " +
            $"monitor=({info.Monitor.Left},{info.Monitor.Top},{info.Monitor.Right},{info.Monitor.Bottom}) " +
            $"covers={covers} dpi={dpi} taskbarAutoHide={autoHide}";

        ProbeLog.Info(message);
        Captured?.Invoke(message);
    }

    private static bool IsTaskbarAutoHide()
    {
        var data = new NativeMethods.AppBarData { Size = Marshal.SizeOf<NativeMethods.AppBarData>() };
        var result = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref data);
        return (result.ToInt64() & NativeMethods.AbsAutoHide) != 0;
    }

    private static string Title(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }
}
