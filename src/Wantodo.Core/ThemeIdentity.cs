using System;
using System.Collections.Generic;

namespace Wantodo.Core;

/// <summary>
/// 色板（palette）id：只决定颜色，不决定材质与形式。
/// 旧字段 <c>theme</c> 里的 github-dark / dracula / cyber 等历史名字在这里一次性映射掉。
/// </summary>
public static class PaletteIds
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Latte = "latte";
    public const string Frappe = "frappe";
    public const string Macchiato = "macchiato";
    public const string Mocha = "mocha";
    public const string Neon = "neon";

    /// <summary>自定义两色的伪色板：只在 <c>customBackground/customAccent</c> 都有值时出现。</summary>
    public const string Custom = "custom";

    /// <summary>设置页可选的固定色板（不含 custom）。顺序即展示顺序。</summary>
    public static readonly IReadOnlyList<string> Selectable = new[]
    {
        Light, Dark, Latte, Frappe, Macchiato, Mocha, Neon,
    };

    public static bool IsKnown(string? id) =>
        id is not null && Selectable.Contains(id.Trim().ToLowerInvariant());

    /// <summary>
    /// 旧 id / 大小写 / 空白 → 规范色板 id；未知一律 <see cref="Dark"/>（V-03 的 <c>theme=SkyNet</c> 走这里）。
    /// 迁移表在 <see cref="ThemePalette.MigrateThemeId"/>，这里只做一次归一，不重复维护映射。
    /// </summary>
    public static string Parse(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Dark;
        }

        var normalized = id.Trim();
        if (string.Equals(normalized, Custom, StringComparison.OrdinalIgnoreCase))
        {
            return Custom;
        }

        var migrated = ThemePalette.MigrateThemeId(normalized);
        return IsKnown(migrated) ? migrated : Dark;
    }
}

/// <summary>材质主题（theme）id：决定圆角、描边、透明与阴影，不决定颜色。</summary>
public static class ThemeIds
{
    /// <summary>液态玻璃（0.7.0 唯一主题）：颜色来自色板，材质走 DWM 分层降级链。</summary>
    public const string LiquidGlass = "liquidGlass";

    /// <summary>纯色兜底：探针全失败 / 关掉效果时用，等同于旧的 flat。</summary>
    public const string Solid = "solid";

    public static readonly IReadOnlyList<string> Selectable = new[] { LiquidGlass };

    public static bool IsKnown(string? id) =>
        id is not null && Selectable.Contains(id.Trim().ToLowerInvariant());

    /// <summary>未知（含旧的 flat/brutal/glass/neon/clay/neumorph）一律 <see cref="LiquidGlass"/>。</summary>
    public static string Parse(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return LiquidGlass;
        }

        var normalized = id.Trim().ToLowerInvariant();
        return normalized switch
        {
            "liquidglass" => LiquidGlass,
            Solid => Solid,
            _ => LiquidGlass,
        };
    }
}

/// <summary>
/// 资源键的唯一真相：<see cref="Wantodo.App.Appearance.ThemeService"/> 只准写这里列出的键，
/// XAML 只准 DynamicResource 引用这些键（见 plan-0.7.0 §3.2 三道防线）。
/// </summary>
public static class ThemeResourceKeys
{
    public const string WindowBackground = "WindowBackground";
    public const string CardBackground = "CardBackground";
    public const string TextPrimary = "TextPrimary";
    public const string TextSecondary = "TextSecondary";
    public const string TextMuted = "TextMuted";
    public const string Divider = "Divider";
    public const string Accent = "Accent";
    public const string OnAccent = "OnAccent";
    public const string Hover = "Hover";
    public const string Selected = "Selected";

    public const string SurfaceCard = "SurfaceCard";
    public const string OnSurfaceCard = "OnSurfaceCard";
    public const string BorderSubtle = "BorderSubtle";
    public const string BorderActive = "BorderActive";
    public const string AccentSoft = "AccentSoft";
    public const string Glow = "Glow";

    public const string CornerRadiusDouble = "ThemeCornerRadiusDouble";
    public const string Elevation = "ThemeElevation";

    public const string StrokeBrush = "ThemeStrokeBrush";
    public const string StrokeWidth = "ThemeStrokeWidth";
    public const string StrokeThickness = "ThemeStrokeThickness";
    public const string CornerRadius = "ThemeCornerRadius";
    public const string TextureBrush = "ThemeTextureBrush";
    public const string CardBorderBrush = "ThemeCardBorderBrush";
    public const string CardBorderThickness = "ThemeCardBorderThickness";
    public const string CardCornerRadius = "ThemeCardCornerRadius";
    public const string GlowVisible = "ThemeGlowVisible";
    public const string ShadowBrush = "ThemeShadowBrush";
    public const string ShadowOffset = "ThemeShadowOffset";
    public const string CardAlpha = "ThemeCardAlpha";

    /// <summary>全部键（顺序稳定，便于测试与文档对照）。</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        WindowBackground, CardBackground, TextPrimary, TextSecondary, TextMuted, Divider,
        Accent, OnAccent, Hover, Selected,
        SurfaceCard, OnSurfaceCard, BorderSubtle, BorderActive, AccentSoft, Glow,
        CornerRadiusDouble, Elevation,
        StrokeBrush, StrokeWidth, StrokeThickness, CornerRadius, TextureBrush,
        CardBorderBrush, CardBorderThickness, CardCornerRadius, GlowVisible, ShadowBrush,
        ShadowOffset, CardAlpha,
    };
}
