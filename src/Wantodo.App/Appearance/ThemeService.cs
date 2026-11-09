using System.Windows;
using System.Windows.Media;
using Wantodo.Core;

namespace Wantodo.App.Appearance;

/// <summary>
/// 把 <see cref="ThemePalette"/> 算出来的颜色写进 <see cref="Application.Resources"/>。
/// XAML 里这 7 个画刷一律用 DynamicResource 引用，换主题时不用重建窗口。
/// </summary>
public static class ThemeService
{
    public static void Apply(Board board)
    {
        Apply(ThemePalette.For(board.Theme, board.Tone));
    }

    public static void Apply(ThemeColors colors)
    {
        var resources = Application.Current?.Resources;
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
    }

    public static SolidColorBrush Brush(Rgb color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
