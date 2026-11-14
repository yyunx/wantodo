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

        var effects = board.ThemeEffects
            ? ThemePalette.EffectsFor(colors.Style)
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

        var accent = Brush(colors.Accent);
        var stroke = effects.StrokeHex is not null && ThemePalette.TryParseHex(effects.StrokeHex, out var strokeColor)
            ? Brush(strokeColor)
            : accent;

        resources["ThemeStrokeBrush"] = stroke;
        resources["ThemeStrokeWidth"] = effects.StrokeWidth;
        resources["ThemeStrokeThickness"] = new System.Windows.Thickness(effects.StrokeWidth);
        resources["ThemeCornerRadius"] = new System.Windows.CornerRadius(effects.CornerRadius);
        resources["ThemeTextureBrush"] = TextureBrush(effects.TextureKey);
    }

    public static SolidColorBrush Brush(Rgb color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
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
