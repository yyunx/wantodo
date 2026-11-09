using System;
using System.Collections.Generic;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Wantodo.App.Interop;

/// <summary>
/// Explorer 重启后 Progman 会换一个新句柄，旧 owner 关系失效，窗口会掉到最上层。
/// 监听 Shell 广播的 "TaskbarCreated"，收到后分几次重挂（Explorer 恢复桌面图标有延迟）。
/// Phase 0 实测：300 / 1500 / 4000ms 三次重挂是幂等的，重复挂不会闪。
/// </summary>
internal sealed class ShellRestartWatcher : IDisposable
{
    private static readonly int[] RetryDelaysMilliseconds = { 300, 1500, 4000 };

    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private readonly uint _message;
    private readonly Action _onShellRestart;
    private readonly List<DispatcherTimer> _timers = new();
    private bool _disposed;

    private ShellRestartWatcher(HwndSource source, uint message, Action onShellRestart)
    {
        _source = source;
        _message = message;
        _onShellRestart = onShellRestart;
        _hook = OnMessage;
        _source.AddHook(_hook);
    }

    public static ShellRestartWatcher? Install(IntPtr hwnd, Action onShellRestart)
    {
        var source = HwndSource.FromHwnd(hwnd);
        if (source is null)
        {
            AppLog.Warn("拿不到 HwndSource，跳过 TaskbarCreated 监听");
            return null;
        }

        var message = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        if (message == 0)
        {
            AppLog.Warn("注册 TaskbarCreated 消息失败，Explorer 重启后需要手动重启本程序");
            return null;
        }

        AppLog.Info($"已监听 TaskbarCreated (0x{message:X})：Explorer 重启后会自动重挂桌面层");
        return new ShellRestartWatcher(source, message, onShellRestart);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_message && !_disposed)
        {
            AppLog.Warn("收到 TaskbarCreated：Explorer 重启了，准备重挂");
            ScheduleRetries();
        }

        return IntPtr.Zero;
    }

    private void ScheduleRetries()
    {
        foreach (var delay in RetryDelaysMilliseconds)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(delay),
            };

            timer.Tick += (sender, _) =>
            {
                if (sender is DispatcherTimer t)
                {
                    t.Stop();
                    _timers.Remove(t);
                }

                if (!_disposed)
                {
                    _onShellRestart();
                }
            };

            _timers.Add(timer);
            timer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var timer in _timers)
        {
            timer.Stop();
        }

        _timers.Clear();
        _source.RemoveHook(_hook);
    }
}
