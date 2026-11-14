using System;
using System.Diagnostics;
using System.Text;

namespace Wantodo.App.Interop;

/// <summary>
/// 前台窗口是否盖满所在显示器（Phase 16 的 covers 判定）。全屏时顶边热区要让路，
/// 否则全屏视频/游戏顶边 2 px 的点击会被偷走。
/// 桌面（Progman/WorkerW）、本进程自己的窗口不算「全屏」。
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly uint _ownProcessId = (uint)Process.GetCurrentProcess().Id;
    private readonly NativeMethods.WinEventProc _callback;
    private IntPtr _hook;

    public ForegroundWatcher()
    {
        _callback = OnForegroundChanged;
    }

    /// <summary>覆盖状态变化：true = 有窗口盖满屏幕。</summary>
    public event Action<bool>? CoversChanged;

    public bool Covers { get; private set; }

    public bool Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        if (_hook == IntPtr.Zero)
        {
            AppLog.Warn("前台窗口监听安装失败：全屏时热区不会自动让路");
            return false;
        }

        Update(NativeMethods.GetForegroundWindow());
        return true;
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        try
        {
            Update(hwnd);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"前台窗口判定异常（已忽略）：{ex.Message}");
        }
    }

    private void Update(IntPtr hwnd)
    {
        bool covers = ComputeCovers(hwnd);
        if (covers == Covers)
        {
            return;
        }

        Covers = covers;
        CoversChanged?.Invoke(covers);
    }

    private bool ComputeCovers(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        string className = NativeMethods.ClassNameOf(hwnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == _ownProcessId)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT window))
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        NativeMethods.RECT screen = info.rcMonitor;
        return window.Left <= screen.Left
            && window.Top <= screen.Top
            && window.Right >= screen.Right
            && window.Bottom >= screen.Bottom;
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
