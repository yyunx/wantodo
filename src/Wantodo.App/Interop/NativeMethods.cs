using System;
using System.Text;

namespace Wantodo.App.Interop;

/// <summary>
/// 所有 Win32 P/Invoke 的唯一出口（AGENTS.md 硬规则 1：视图与 ViewModel 不得直接 DllImport）。
/// 只放 Phase 1 真正用到的声明，不做"顺手多声明几个"。
/// </summary>
internal static class NativeMethods
{
    internal const int GWLP_HWNDPARENT = -8;
    internal const int GWL_EXSTYLE = -20;

    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const uint WS_EX_LAYERED = 0x00080000;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    internal const uint SMTO_NORMAL = 0x0000;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>未公开的 Shell 消息，旧版 Windows 用它催生壁纸层 WorkerW。新版可能无效，只记日志。</summary>
    internal const uint WM_SPAWN_WORKERW = 0x052C;

    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? className, string? windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string message);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMilliseconds,
        out IntPtr result);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint VK_SPACE = 0x20;

    /// <summary>全局快捷键「快速记一条」用的 id；一个窗口内唯一即可。</summary>
    internal const int QuickCaptureHotKeyId = 0x1A70;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int command);

    /// <summary>不抢焦点地把窗口显示出来（托盘召回时用）。</summary>
    internal static void ShowWithoutActivating(IntPtr hWnd) => ShowWindow(hWnd, SW_SHOWNOACTIVATE);

    // ---------- 单实例：第二次启动把已有窗口叫到前面 ----------

    /// <summary>第二个实例启动时发给第一个实例的消息（同名注册，两个进程拿到同一个 id）。</summary>
    internal static readonly int ShowWantodoMessage = (int)RegisterWindowMessage("Wantodo.Show");

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---------- 顶边下拉面板：低级鼠标钩子（Phase 17） ----------

    internal const int WH_MOUSE_LL = 14;

    internal const uint WM_LBUTTONDOWN = 0x0201;

    internal const uint WS_EX_NOACTIVATE = 0x08000000;

    internal const uint WS_EX_TOOLWINDOW = 0x00000080;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    // ---------- 全屏覆盖检测（Phase 16 结论）：前台窗口是否盖满整块屏幕 ----------

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    internal delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint threadId, uint time);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    internal static string ClassNameOf(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(256);
        var length = GetClassName(hWnd, buffer, buffer.Capacity);
        return length <= 0 ? string.Empty : buffer.ToString(0, length);
    }
}
