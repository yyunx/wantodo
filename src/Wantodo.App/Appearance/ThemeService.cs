using System;
using System.Windows.Media;
using Wantodo.App.Interop;
using Wantodo.Core;

namespace Wantodo.App.Appearance;

/// <summary>
/// 把 <see cref="ThemePalette"/> 算出来的颜色与形式感参数写进
/// <see cref="System.Windows.Application.Resources"/>。XAML 里一律用 DynamicResource 引用，
/// 换主题时不用重建窗口。
/// </summary>
public static class ThemeService
{
    /// <summary>
    /// 资源换完一批之后的通知。XAML 里用 DynamicResource 的元素会自己刷新；
    /// 程序化建出来的元素（卡片、图元）拿到的是 <c>FindResource</c> 的快照画刷，
    /// 必须靠这个事件重建一次才能适配新主题（用户反馈：换背景后卡片要等下一次操作才变色）。
    /// </summary>
    public static event Action? Applied;

    public static void Apply(Board board)
    {
        var colors = ThemePalette.For(
            board.PaletteId,
            board.Tone,
            SystemTheme.IsLightApplicationTheme(),
            board.CustomBackground,
            board.CustomAccent);

        // 0.7.0：材质由 themeId 决定（当前只有液态玻璃），关掉效果退回纯色。
        var liquidGlass = string.Equals(board.ThemeId, Core.ThemeIds.LiquidGlass, StringComparison.Ordinal)
            && !board.ReducedEffects;
        var materialSupported = GlassMaterial.IsSupported(out var materialReason);
        GlassState.Enabled = liquidGlass && materialSupported;
        GlassState.Reason = liquidGlass
            ? materialReason
            : "主题不是液态玻璃或已关闭效果";

        var effects = board.ReducedEffects
            ? ThemeEffects.Flat
            : ThemePalette.EffectsFor(ThemeForm.Glass);

        Apply(colors, effects);
    }

    public static void Apply(ThemeColors colors) => Apply(colors, ThemeEffects.Flat);

    public static void Apply(ThemeColors colors, ThemeEffects effects)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        resources[ThemeResourceKeys.WindowBackground] = Brush(colors.WindowBackground);
        resources[ThemeResourceKeys.CardBackground] = Brush(colors.CardBackground);
        resources[ThemeResourceKeys.TextPrimary] = Brush(colors.TextPrimary);
        resources[ThemeResourceKeys.TextSecondary] = Brush(colors.TextSecondary);
        resources[ThemeResourceKeys.TextMuted] = Brush(colors.TextMuted);
        resources[ThemeResourceKeys.Divider] = Brush(colors.Divider);
        resources[ThemeResourceKeys.Accent] = Brush(colors.Accent);
        resources[ThemeResourceKeys.OnAccent] = Brush(colors.OnAccent);
        resources[ThemeResourceKeys.Hover] = Brush(colors.Hover);
        resources[ThemeResourceKeys.Selected] = Brush(colors.Selected);

        resources[ThemeResourceKeys.SurfaceCard] = Brush(colors.SurfaceCard, effects.CardAlpha);
        resources[ThemeResourceKeys.OnSurfaceCard] = Brush(colors.OnSurfaceCard);
        resources[ThemeResourceKeys.BorderSubtle] = Brush(colors.BorderSubtle);
        resources[ThemeResourceKeys.BorderActive] = Brush(colors.BorderActive);
        resources[ThemeResourceKeys.AccentSoft] = Brush(colors.AccentSoft);
        resources[ThemeResourceKeys.Glow] = colors.Glow is { } glow ? Brush(glow) : Brushes.Transparent;
        resources[ThemeResourceKeys.CornerRadiusDouble] = colors.CornerRadius;
        resources[ThemeResourceKeys.Elevation] = colors.Elevation;

        var accent = Brush(colors.Accent);
        var stroke = effects.StrokeHex is not null && ThemePalette.TryParseHex(effects.StrokeHex, out var strokeColor)
            ? Brush(strokeColor)
            : accent;

        resources[ThemeResourceKeys.StrokeBrush] = stroke;
        resources[ThemeResourceKeys.StrokeWidth] = effects.StrokeWidth;
        resources[ThemeResourceKeys.StrokeThickness] = new System.Windows.Thickness(effects.StrokeWidth);
        resources[ThemeResourceKeys.CornerRadius] = new System.Windows.CornerRadius(effects.CornerRadius);
        resources[ThemeResourceKeys.TextureBrush] = TextureBrush(effects.TextureKey);
        resources[ThemeResourceKeys.CardBorderBrush] = effects.StrokeHex is not null && ThemePalette.TryParseHex(effects.StrokeHex, out var cardStroke)
            ? Brush(cardStroke)
            : Brush(colors.BorderSubtle);
        resources[ThemeResourceKeys.CardBorderThickness] = new System.Windows.Thickness(Math.Max(1, effects.StrokeWidth));
        resources[ThemeResourceKeys.CardCornerRadius] = new System.Windows.CornerRadius(effects.CornerRadius == 0 ? 0 : Math.Min(effects.CornerRadius, 10));
        resources[ThemeResourceKeys.GlowVisible] = effects.Form == ThemeForm.Neon
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        resources[ThemeResourceKeys.ShadowBrush] = effects.ShadowOpacity > 0
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)Math.Round(255 * effects.ShadowOpacity), 0, 0, 0))
            : Brushes.Transparent;
        resources[ThemeResourceKeys.ShadowOffset] = effects.ShadowOffset;
        resources[ThemeResourceKeys.CardAlpha] = effects.CardAlpha;

        // L1/L2 层：只有材质真的启用时才半透明，否则用不透明底，避免「假装玻璃」
        resources[ThemeResourceKeys.GlassContentBrush] = GlassState.Enabled
            ? Brush(colors.SurfaceCard, 0.72)
            : Brush(colors.WindowBackground);
        resources[ThemeResourceKeys.GlassControlBrush] = GlassState.Enabled
            ? Brush(colors.SurfaceCard, 0.86)
            : Brush(colors.CardBackground);

        Applied?.Invoke();
    }

    public static SolidColorBrush Brush(Rgb color) => Brush(color, 1);

    public static SolidColorBrush Brush(Rgb color, double alpha)
    {
        var a = (byte)Math.Clamp(Math.Round(255 * alpha), 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Brush TextureBrush(string? key)
    {
        if (key is null)
        {
            return Brushes.Transparent;
        }

        return System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }
}
