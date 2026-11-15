using System;

namespace Wantodo.App.Interop;

/// <summary>
/// 窗口级材质（0.7.0 P0.5 探针结论见 docs/verify/0.7.0/p0.5-probe.md）：
/// <list type="bullet">
/// <item>Win11 22621+ 用 <c>DWMWA_SYSTEMBACKDROP_TYPE(38)=DWMSBT_MAINWINDOW(2)</c> 拿 Mica；</item>
/// <item>Win11 22000–22620 旧属性 <c>DWMWA_MICA_EFFECT(1029)=1</c>（未在本机验证，失败即降级）；</item>
/// <item>其余（Win10 1803–19045 等）一律返回 false，由调用方退回纯色 + WARN。</item>
/// </list>
/// 注意：只有「WindowChrome.GlassFrameThickness=-1 + Window.Background=null」时材质才可见
/// （探针实测：普通 WPF 窗口无论怎么设 DWM 属性，客户区都是不透明像素，材质被完全盖住）。
/// </summary>
internal static class GlassMaterial
{
    public const int DwmwaSystemBackdropType = 38;
    public const int DwmsbtMainWindow = 2;
    public const int DwmwaMicaEffect = 1029;

    private const int Windows11FirstBuild = 22000;
    private const int MicaBackdropFirstBuild = 22621;

    /// <summary>本机系统能不能出材质（不调用 API，只看版本，用于「要不要把窗口变透明」的决策）。</summary>
    public static bool IsSupported(out string reason)
    {
        var build = Environment.OSVersion.Version.Build;
        if (build >= MicaBackdropFirstBuild)
        {
            reason = $"Win11 build {build}：走 DWMWA_SYSTEMBACKDROP_TYPE=Mica";
            return true;
        }

        if (build >= Windows11FirstBuild)
        {
            reason = $"Win11 build {build}：走旧属性 DWMWA_MICA_EFFECT（本机未验证）";
            return true;
        }

        reason = $"build {build} 没有文档化的窗口材质 API（需要 Win11 22000+），降级纯色";
        return false;
    }

    /// <summary>请求材质。返回 true 才允许把窗口背景置空，否则会漏出桌面。</summary>
    public static bool TryApply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            AppLog.Warn("材质：空句柄，跳过");
            return false;
        }

        if (!IsSupported(out var reason))
        {
            AppLog.Warn($"材质：{reason}");
            return false;
        }

        try
        {
            var build = Environment.OSVersion.Version.Build;
            var value = build >= MicaBackdropFirstBuild ? DwmsbtMainWindow : 1;
            var attribute = build >= MicaBackdropFirstBuild ? DwmwaSystemBackdropType : DwmwaMicaEffect;
            var hr = NativeMethods.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

            if (hr == 0)
            {
                AppLog.Info($"材质：Mica 已请求 attr={attribute} value={value}（{reason}）");
                return true;
            }

            AppLog.Warn($"材质：DwmSetWindowAttribute attr={attribute} 失败 hr=0x{hr:X8}，降级纯色");
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Warn("材质：dwmapi 不可用，降级纯色");
            return false;
        }
    }
}
