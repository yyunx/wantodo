using System;
using System.Text;
using static DesktopLayerProbe.Interop.NativeMethods;

namespace DesktopLayerProbe.Interop;

internal enum LayerMode
{
    /// <summary>挂到 WorkerW/Progman（owner 关系）——目标形态。</summary>
    WorkerWOwner = 1,

    /// <summary>只设 owner=Progman，不碰 WorkerW。</summary>
    ProgmanOwner = 2,

    /// <summary>普通置顶窗口（对照组）。</summary>
    Topmost = 3,

    /// <summary>普通窗口，不置顶（对照组）。</summary>
    Normal = 4,

    /// <summary>挂到桌面层后强行压到 z 序底部（另一种"像壁纸"的做法）。</summary>
    WorkerWOwnerBottom = 5,
}

internal static class LayerModeText
{
    public static string Describe(this LayerMode mode) => mode switch
    {
        LayerMode.WorkerWOwner => "WorkerW owner（桌面层）",
        LayerMode.ProgmanOwner => "Progman owner（近似桌面层）",
        LayerMode.Topmost => "Topmost（置顶，对照组）",
        LayerMode.Normal => "Normal（普通窗口，对照组）",
        LayerMode.WorkerWOwnerBottom => "WorkerW owner + HWND_BOTTOM",
        _ => mode.ToString(),
    };
}

/// <summary>
/// 桌面层挂载的全部逻辑。产品化时整体搬进 src/Wantodo.App/Interop/。
/// 规矩：所有 P/Invoke 只经过 NativeMethods，业务层只调这里的方法。
/// </summary>
internal sealed class DesktopLayerService
{
    private readonly IntPtr _hwnd;
    private ShellTopologySnapshot _topology;

    public DesktopLayerService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _topology = ShellTopologySnapshot.Collect(hwnd);
    }

    public LayerMode Mode { get; private set; } = LayerMode.WorkerWOwner;

    public ShellTopologySnapshot Topology => _topology;

    public string TopologySummary => _topology.Summary;

    /// <summary>向 Progman 发未公开消息 0x052C，催生用于壁纸的 WorkerW。
    /// 注意：在某些新版 Windows 上这一步可能没有任何可观察效果，探针必须容忍它失败。</summary>
    public bool TrySpawnWorkerW()
    {
        if (_topology.Progman == IntPtr.Zero)
        {
            ProbeLog.Warn("Progman 不存在？");
            return false;
        }

        var ok = SendMessageTimeout(
            _topology.Progman,
            WM_SPAWN_WORKERW,
            IntPtr.Zero,
            IntPtr.Zero,
            SMTO_NORMAL | SMTO_ABORTIFHUNG,
            2000,
            out var result);

        var succeeded = ok != IntPtr.Zero;
        ProbeLog.Info($"SendMessageTimeout(Progman, 0x052C) → ok={succeeded}, result={Hex(result)}, lastErr={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        return succeeded;
    }

    public void RefreshTopology()
    {
        _topology = ShellTopologySnapshot.Collect(_hwnd);
        ProbeLog.Write("TOPO", _topology.Summary);
        ProbeLog.Block("TOPO", _topology.Detail);
    }

    /// <summary>
    /// 切换到指定层级。每次都先"彻底复位"，避免上一档的 owner / TOPMOST 残留，
    /// 否则切换测试本身不可信。
    /// </summary>
    public void Apply(LayerMode mode, string reason)
    {
        ProbeLog.Section($"Apply {mode}（{mode.Describe()}）reason={reason}");

        // 1) 复位：清 owner，清 TOPMOST，保证窗口在 z 序正常参与
        SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
        SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        IntPtr owner = mode switch
        {
            LayerMode.WorkerWOwner or LayerMode.WorkerWOwnerBottom => _topology.MountCandidate,
            LayerMode.ProgmanOwner => _topology.Progman,
            _ => IntPtr.Zero,
        };

        if (owner != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, owner);
            ProbeLog.Info($"SetWindowLongPtr(GWLP_HWNDPARENT) ← {Hex(owner)} ({ClassOf(owner)}), lastErr={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }
        else
        {
            ProbeLog.Info("保持 owner = 0（真正的顶层窗口）");
        }

        if (mode == LayerMode.Topmost)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else if (mode == LayerMode.WorkerWOwnerBottom)
        {
            SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        Mode = mode;
        RefreshZOrder();
        ProbeLog.Block("STATE", DescribeState());
    }

    /// <summary>只刷新顶层窗口 z 序（切换层级后 zIndex 才会准确），不做完整拓扑 dump。</summary>
    private void RefreshZOrder()
    {
        _topology.AllTopLevel.Clear();
        EnumWindows((h, _) =>
        {
            _topology.AllTopLevel.Add(h);
            return true;
        }, IntPtr.Zero);
    }

    public void Show()
    {
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        ProbeLog.Info("SetWindowPos(SWP_SHOWWINDOW) 已调用");
    }

    public string DescribeState()
    {
        var sb = new StringBuilder();
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        var owner = GetWindowLongPtr(_hwnd, GWLP_HWNDPARENT);

        sb.Append("mode=").Append(Mode)
          .Append(" hwnd=").Append(Hex(_hwnd))
          .Append(" visible=").Append(IsWindowVisible(_hwnd))
          .Append(" owner=").Append(Hex(owner)).Append('(').Append(ClassOf(owner)).Append(')')
          .Append(" TOPMOST=").Append((ex & WS_EX_TOPMOST) != 0)
          .Append(" TOOLWINDOW=").Append((ex & WS_EX_TOOLWINDOW) != 0)
          .Append(" LAYERED=").Append((ex & WS_EX_LAYERED) != 0);

        if (GetWindowRect(_hwnd, out var r))
        {
            sb.Append(" rect=").Append(r.Left).Append(',').Append(r.Top)
              .Append(' ').Append(r.Width).Append('x').Append(r.Height);
        }

        sb.Append(" dpi=").Append(GetDpiForWindow(_hwnd));

        var mon = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(mon, ref mi))
            {
                sb.Append(" monitor=").Append(mi.szDevice)
                  .Append(" work=").Append(mi.rcWork.Width).Append('x').Append(mi.rcWork.Height);
            }
        }

        sb.Append(" zIndex=").Append(_topology.AllTopLevel.IndexOf(_hwnd)).Append('/').Append(_topology.AllTopLevel.Count);
        sb.Append(" mountCandidate=").Append(Hex(_topology.MountCandidate)).Append('(').Append(ClassOf(_topology.MountCandidate)).Append(')');
        return sb.ToString();
    }
}
