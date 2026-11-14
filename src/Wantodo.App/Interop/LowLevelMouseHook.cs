using System;
using System.Runtime.InteropServices;

namespace Wantodo.App.Interop;

/// <summary>
/// 低级鼠标钩子：只用来判断「点击落在面板外」，不拦截、不改事件。
/// 回调在安装线程（UI 线程）上跑；异常必须自己咽掉，不能让钩子把进程带崩。
/// </summary>
internal sealed class LowLevelMouseHook : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private IntPtr _handle;

    public LowLevelMouseHook()
    {
        _callback = OnHook;
    }

    /// <summary>左键按下，参数是屏幕物理像素坐标。</summary>
    public event Action<int, int>? LeftButtonDown;

    public bool IsRunning => _handle != IntPtr.Zero;

    public bool Start()
    {
        if (_handle != IntPtr.Zero)
        {
            return true;
        }

        _handle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_handle == IntPtr.Zero)
        {
            AppLog.Warn($"低级鼠标钩子安装失败：{Marshal.GetLastWin32Error()}");
            return false;
        }

        return true;
    }

    private IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                LeftButtonDown?.Invoke(data.pt.X, data.pt.Y);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"鼠标钩子回调异常（已忽略）：{ex.Message}");
        }

        return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }
}
