using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using Wantodo.App.Interop;
using Wantodo.Core;

namespace Wantodo.App.Appearance;

/// <summary>
/// 把一个普通 WPF 窗口变成「能透出 DWM 材质」的窗口。
/// 探针实测（docs/verify/0.7.0/p0.5-probe.md）：
/// <list type="bullet">
/// <item>只调 DwmSetWindowAttribute 没用——WPF 客户区是不透明像素，材质被完全盖住（mica/none 两张截图逐像素相同）；</item>
/// <item>必须同时 <c>WindowChrome.GlassFrameThickness = -1</c> + <c>Window.Background = null</c>；</item>
/// <item>这样改之后控件照常可点（探针里模拟真实点击，按钮 Click 正常触发）。</item>
/// </list>
/// 注意玻璃区里的内容必须自己带半透明底色（ThemeGlassContentBrush / ThemeGlassControlBrush），
/// 否则会看到完全透底的桌面。
/// </summary>
internal static class GlassSurface
{
    /// <summary>窗口 Show 之前调用：装 WindowChrome、把背景置空。材质是否可用由 <see cref="GlassState"/> 决定。</summary>
    public static void Prepare(Window window)
    {
        if (!GlassState.Enabled)
        {
            AppLog.Info($"[玻璃] {window.GetType().Name} 不启用材质：{GlassState.Reason}");
            return;
        }

        WindowChrome.SetWindowChrome(
            window,
            new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });

        window.Background = null;
        AppLog.Info($"[玻璃] {window.GetType().Name} 已准备玻璃窗口：{GlassState.Reason}");
    }

    /// <summary>SourceInitialized 之后调用：真正向 DWM 请求材质；失败就把背景恢复成不透明（否则会漏底）。</summary>
    public static bool TryApply(Window window)
    {
        if (!GlassState.Enabled)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (GlassMaterial.TryApply(hwnd))
        {
            return true;
        }

        if (window.TryFindResource(ThemeResourceKeys.WindowBackground) is System.Windows.Media.Brush opaque)
        {
            window.Background = opaque;
        }

        AppLog.Warn($"[玻璃] {window.GetType().Name} 材质请求失败，已退回不透明背景");
        return false;
    }
}
