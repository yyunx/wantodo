using System;
using System.Collections.Generic;
using System.Text;
using static DesktopLayerProbe.Interop.NativeMethods;

namespace DesktopLayerProbe.Interop;

/// <summary>
/// 一次 Explorer 桌面层拓扑快照。
/// 注意：Progman / WorkerW / SHELLDLL_DefView 的层级关系属于未公开的 Shell 内部结构，
/// 不同 Windows 版本拓扑不同（这是探针存在的全部理由），所以这里只做"观测 + 记录"，不做假设。
/// </summary>
internal sealed class ShellTopologySnapshot
{
    public IntPtr Progman;
    public IntPtr DefView;
    public IntPtr DefViewHost;
    public List<IntPtr> TopLevelWorkerWs = new();
    public List<IntPtr> AllTopLevel = new();
    public string Summary = string.Empty;
    public string Detail = string.Empty;

    /// <summary>候选挂载点：优先"带图标视图的那一层"，退到 Progman。</summary>
    public IntPtr MountCandidate => DefViewHost != IntPtr.Zero ? DefViewHost : Progman;

    public int IndexOfMountCandidate => AllTopLevel.IndexOf(MountCandidate);

    public static ShellTopologySnapshot Collect(IntPtr ownWindow)
    {
        var s = new ShellTopologySnapshot();

        s.Progman = FindWindow("Progman", null);

        EnumWindows((h, _) =>
        {
            s.AllTopLevel.Add(h);
            if (ClassOf(h) == "WorkerW")
            {
                s.TopLevelWorkerWs.Add(h);
            }

            return true;
        }, IntPtr.Zero);

        // 1) Progman 自己带 SHELLDLL_DefView？
        if (s.Progman != IntPtr.Zero)
        {
            s.DefView = FindWindowEx(s.Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (s.DefView != IntPtr.Zero)
            {
                s.DefViewHost = s.Progman;
            }
        }

        // 2) 否则找哪个顶层 WorkerW 带 SHELLDLL_DefView
        if (s.DefView == IntPtr.Zero)
        {
            foreach (var w in s.TopLevelWorkerWs)
            {
                var d = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (d != IntPtr.Zero)
                {
                    s.DefView = d;
                    s.DefViewHost = w;
                    break;
                }
            }
        }

        s.BuildDetail(ownWindow);
        return s;
    }

    private void BuildDetail(IntPtr ownWindow)
    {
        var sb = new StringBuilder();
        var s2 = new StringBuilder();

        s2.Append("Progman=").Append(Hex(Progman))
          .Append(" DefView=").Append(Hex(DefView))
          .Append(" DefViewHost=").Append(Hex(DefViewHost))
          .Append("(").Append(ClassOf(DefViewHost)).Append(')')
          .Append(" WorkerW(顶层)=").Append(TopLevelWorkerWs.Count)
          .Append(" 候选挂载点=").Append(Hex(MountCandidate))
          .Append(" z序=").Append(IndexOfMountCandidate);

        sb.AppendLine("---- 顶层窗口 z 序（最上 → 最下，最多 40）----");
        for (var i = 0; i < AllTopLevel.Count && i < 40; i++)
        {
            var h = AllTopLevel[i];
            sb.Append("  #").Append(i.ToString("D2")).Append(' ')
              .Append(Hex(h)).Append(' ')
              .Append(ClassOf(h));

            var t = TitleOf(h);
            if (t.Length > 0)
            {
                sb.Append(" \"").Append(t.Length > 40 ? t.Substring(0, 40) : t).Append('"');
            }

            if (h == Progman) sb.Append("   <== Progman");
            if (h == DefViewHost && DefViewHost != Progman) sb.Append("   <== DefView 宿主 WorkerW");
            if (h == ownWindow) sb.Append("   <== 本探针窗口");
            if (!IsWindowVisible(h)) sb.Append("   [隐藏]");
            sb.AppendLine();
        }

        if (TopLevelWorkerWs.Count > 0)
        {
            sb.AppendLine("---- 顶层 WorkerW 各自直接子窗口（按 z 序）----");
            foreach (var w in TopLevelWorkerWs)
            {
                sb.Append("  ").Append(Hex(w)).Append(IsWindowVisible(w) ? " 可见" : " 隐藏").AppendLine();
                EnumChildWindows(w, (c, _) =>
                {
                    var cls = ClassOf(c);
                    if (cls == "WorkerW")
                    {
                        return true; // 嵌套 WorkerW 单独不展开
                    }

                    sb.Append("      ").Append(Hex(c)).Append(' ').Append(cls).AppendLine();
                    return true;
                }, IntPtr.Zero);
            }
        }

        Summary = s2.ToString();
        Detail = sb.ToString();
    }
}
