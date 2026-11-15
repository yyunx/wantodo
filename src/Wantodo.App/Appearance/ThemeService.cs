using System.Windows.Media;
using Wantodo.Core;

namespace Wantodo.App.Appearance;

/// <summary>
/// 把 <see cref="ThemePalette"/> 算出来的颜色与形式感参数写进
/// <see cref="System.Windows.Application.Resources"/>。XAML 里一律用 DynamicResource 引用，
/// 换主题时不用重建窗口。
/// </summary>
public static class ThemeService
{
    public static void Apply(Board board)
    {
        var colors = ThemePalette.For(
            board.Theme,
            board.Tone,
            SystemTheme.IsLightApplicationTheme(),
            board.CustomBackground,
            board.CustomAccent);

        var form = ThemePalette.ParseForm(board.FormId);
        var effects = board.ThemeEffects || form != ThemeForm.Flat
            ? ThemePalette.EffectsFor(form)
            : ThemeEffects.Flat;

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

        resources["WindowBackground"] = Brush(colors.WindowBackground);
        resources["CardBackground"] = Brush(colors.CardBackground);
        resources["TextPrimary"] = Brush(colors.TextPrimary);
        resources["TextSecondary"] = Brush(colors.TextSecondary);
        resources["TextMuted"] = Brush(colors.TextMuted);
        resources["Divider"] = Brush(colors.Divider);
        resources["Accent"] = Brush(colors.Accent);
        resources["OnAccent"] = Brush(colors.OnAccent);
        resources["Hover"] = Brush(colors.Hover);
        resources["Selected"] = Brush(colors.Selected);

        resources["SurfaceCard"] = Brush(colors.SurfaceCard, effects.CardAlpha);
        resources["OnSurfaceCard"] = Brush(colors.OnSurfaceCard);
        resources["BorderSubtle"] = Brush(colors.BorderSubtle);
        resources["BorderActive"] = Brush(colors.BorderActive);
        resources["AccentSoft"] = Brush(colors.AccentSoft);
        resources["Glow"] = colors.Glow is { } glow ? Brush(glow) : Brushes.Transparent;
        resources["ThemeCornerRadiusDouble"] = colors.CornerRadius;
        resources["ThemeElevation"] = colors.Elevation;

        var accent = Brush(colors.Accent);
        var stroke = effects.StrokeHex is not null && ThemePalette.TryParseHex(effects.StrokeHex, out var strokeColor)
            ? Brush(strokeColor)
            : accent;

        resources["ThemeStrokeBrush"] = stroke;
        resources["ThemeStrokeWidth"] = effects.StrokeWidth;
        resources["ThemeStrokeThickness"] = new System.Windows.Thickness(effects.StrokeWidth);
        resources["ThemeCornerRadius"] = new System.Windows.CornerRadius(effects.CornerRadius);
        resources["ThemeTextureBrush"] = TextureBrush(effects.TextureKey);
        resources["ThemeCardBorderBrush"] = effects.StrokeHex is not null && ThemePalette.TryParseHex(effects.StrokeHex, out var cardStroke)
            ? Brush(cardStroke)
            : Brush(colors.BorderSubtle);
        resources["ThemeCardBorderThickness"] = new System.Windows.Thickness(Math.Max(1, effects.StrokeWidth));
        resources["ThemeCardCornerRadius"] = new System.Windows.CornerRadius(effects.CornerRadius == 0 ? 0 : Math.Min(effects.CornerRadius, 10));
        resources["ThemeGlowVisible"] = effects.Form == ThemeForm.Neon
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        resources["ThemeShadowBrush"] = effects.ShadowOpacity > 0
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)Math.Round(255 * effects.ShadowOpacity), 0, 0, 0))
            : Brushes.Transparent;
        resources["ThemeShadowOffset"] = effects.ShadowOffset;
        resources["ThemeCardAlpha"] = effects.CardAlpha;
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
