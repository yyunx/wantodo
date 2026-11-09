using System;

namespace Wantodo.App.Interop;

/// <summary>
/// 找「桌面宿主窗口」。Phase 0 实测（Win11 26200）：SHELLDLL_DefView 直接在 Progman 下，
/// 顶层 WorkerW 都不持有它，所以 Progman 优先、WorkerW 兜底。详见 docs/verify/phase0.md。
/// </summary>
internal static class DesktopHost
{
    public static IntPtr Find()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            AppLog.Warn("找不到 Progman（Explorer 没在跑？）");
            return IntPtr.Zero;
        }

        if (HasDefView(progman))
        {
            return progman;
        }

        var workerW = IntPtr.Zero;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (NativeMethods.ClassNameOf(hWnd) == "WorkerW" && HasDefView(hWnd))
            {
                workerW = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return workerW != IntPtr.Zero ? workerW : progman;
    }

    /// <summary>旧版 Windows 的壁纸层 WorkerW 可能还没生成；调一次为兼容旧系统，失败无所谓。</summary>
    public static void TrySpawnWorkerW(IntPtr progman)
    {
        if (progman == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var ok = NativeMethods.SendMessageTimeout(
                progman,
                NativeMethods.WM_SPAWN_WORKERW,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.SMTO_NORMAL | NativeMethods.SMTO_ABORTIFHUNG,
                1000,
                out _);
            AppLog.Info($"SendMessageTimeout(Progman, 0x052C) → {ok != IntPtr.Zero}");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"催生 WorkerW 失败（可以忽略）：{ex.Message}");
        }
    }

    private static bool HasDefView(IntPtr hWnd)
        => NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
}
