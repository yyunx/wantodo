using System;
using TopEdgeProbe.Interop;

namespace TopEdgeProbe.Interop;

/// <summary>
/// P1 的第二条路：全局低级鼠标钩子，只在光标第一次进入屏幕顶边物理像素行时计一次。
/// 同时给 P4 提供「点在面板外」的判定。
/// </summary>
internal sealed class MouseEdgeHook : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private IntPtr _hook;
    private bool _inside;
    private int _edgeRow = 2;

    public MouseEdgeHook()
    {
        _proc = Callback;
    }

    public event Action<int, int>? EdgeTouched;

    public event Action<int, int>? LeftButtonDown;

    public int Hits { get; private set; }

    /// <summary>顶边算几物理像素：由主屏缩放换算（150% 时 2 DIP = 3 物理像素）。</summary>
    public int EdgeRow
    {
        get => _edgeRow;
        set => _edgeRow = Math.Max(1, value);
    }

    public void Start()
    {
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl, _proc, NativeMethods.GetModuleHandle(null), 0);
        ProbeLog.Info("mouse-hook: " + (_hook == IntPtr.Zero ? "安装失败" : "已安装"));
    }

    public void Reset() => Hits = 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            if (message == NativeMethods.WmMouseMove)
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
                var inTopRow = data.Point.Y >= 0 && data.Point.Y < _edgeRow;
                if (inTopRow && !_inside)
                {
                    _inside = true;
                    Hits++;
                    EdgeTouched?.Invoke(data.Point.X, data.Point.Y);
                }
                else if (!inTopRow)
                {
                    _inside = false;
                }
            }
            else if (message == NativeMethods.WmLeftButtonDown)
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
                LeftButtonDown?.Invoke(data.Point.X, data.Point.Y);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }
}
