using System;
using System.Globalization;

namespace Wantodo.App.Interop;

/// <summary>
/// 把窗口摆到哪一层。三档语义见 docs/decisions.md D2：
/// Desktop（默认，贴桌面图标层）/ Normal / Topmost。
/// 关键实现：设 <b>owner</b>（GWLP_HWNDPARENT），不是 SetParent —— 子窗口会改几何与生命周期语义。
/// </summary>
internal static class DesktopLayer
{
    public const string SurfaceDesktop = "Desktop";
    public const string SurfaceNormal = "Normal";
    public const string SurfaceTopmost = "Topmost";

    /// <summary>挂到指定层级。可以重复调用（Explorer 重启后句柄变了就得再来一次），幂等。</summary>
    public static bool Attach(IntPtr hwnd, string? surface)
    {
        if (hwnd == IntPtr.Zero)
        {
            AppLog.Warn("Attach 收到空句柄，跳过");
            return false;
        }

        var mode = (surface ?? SurfaceDesktop).Trim();

        // 先彻底复位，避免上一档的 owner / TOPMOST 残留
        _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT, IntPtr.Zero);

        switch (mode.ToUpperInvariant())
        {
            case "DESKTOP":
            {
                var host = DesktopHost.Find();
                if (host == IntPtr.Zero)
                {
                    AppLog.Warn("找不到桌面宿主窗口，这一轮不挂桌面层（窗口会像普通窗口一样）");
                    return false;
                }

                var previous = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT, host);
                if (previous == IntPtr.Zero && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 0)
                {
                    AppLog.Error($"SetWindowLongPtr(owner) 失败 err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                }

                _ = NativeMethods.SetWindowPos(
                    hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

                AppLog.Info($"挂到桌面层 host=0x{host.ToInt64():X}（{NativeMethods.ClassNameOf(host)}）；" + Describe(hwnd));
                return true;
            }

            case "TOPMOST":
            {
                _ = NativeMethods.SetWindowPos(
                    hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                AppLog.Info("窗口改为 Topmost；" + Describe(hwnd));
                return true;
            }

            case "NORMAL":
            {
                _ = NativeMethods.SetWindowPos(
                    hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                AppLog.Info("窗口改为普通；" + Describe(hwnd));
                return true;
            }

            default:
                AppLog.Warn($"未知的 surface=\"{mode}\"，按 Desktop 处理");
                return Attach(hwnd, SurfaceDesktop);
        }
    }

    /// <summary>诊断用：确认到底是 owner 还是子窗口、有没有被误设成分层窗口。</summary>
    public static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "hwnd=0";
        }

        var owner = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT);
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"visible={NativeMethods.IsWindowVisible(hwnd)} owner=0x{(long)owner:X} topmost={(exStyle & NativeMethods.WS_EX_TOPMOST) != 0} layered={(exStyle & NativeMethods.WS_EX_LAYERED) != 0}");
    }

    /// <summary>问 DWM 要圆角。不支持就保持方角，这是允许的降级（decisions.md D3）。</summary>
    public static void TryRoundCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var preference = NativeMethods.DWMWCP_ROUND;
            var hr = NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            if (hr == 0)
            {
                AppLog.Info("DWM 圆角：已请求");
            }
            else
            {
                AppLog.Info($"DWM 圆角不可用 hr=0x{hr:X8}，保持方角");
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Info("dwmapi 不可用，保持方角");
        }
    }
}
