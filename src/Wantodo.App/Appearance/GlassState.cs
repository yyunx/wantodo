using System;

namespace Wantodo.App.Appearance;

/// <summary>
/// 液态玻璃是否真的启用：主题是 liquidGlass、没关效果、且系统有窗口材质 API。
/// <see cref="ThemeService"/> 每次 Apply 时算一遍，窗口首次显示时读它决定要不要把背景置空
/// （探针结论：只有背景置空 Mica 才可见，见 docs/verify/0.7.0/p0.5-probe.md）。
/// </summary>
internal static class GlassState
{
    public static bool Enabled { get; set; }

    public static string Reason { get; set; } = "尚未判定";
}
