namespace Wantodo.Core;

/// <summary>8 位 RGB 颜色。放在 Core 里是为了不依赖 WPF，配色规则可以单测。</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        if (hex is null)
        {
            throw new ArgumentNullException(nameof(hex));
        }

        var value = hex.TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..]; // 忽略 alpha
        }

        if (value.Length != 6)
        {
            throw new FormatException($"颜色必须是 #RRGGBB 或 #AARRGGBB：{hex}");
        }

        return new Rgb(
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16));
    }

    /// <summary>线性插值，<paramref name="t"/> = 0 返回自己，1 返回 <paramref name="other"/>。</summary>
    public Rgb Lerp(Rgb other, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(R + (other.R - R) * t),
            (byte)Math.Round(G + (other.G - G) * t),
            (byte)Math.Round(B + (other.B - B) * t));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>一套主题下的 7 个基础色。</summary>
public sealed record ThemeColors(
    Rgb WindowBackground,
    Rgb CardBackground,
    Rgb TextPrimary,
    Rgb TextSecondary,
    Rgb TextMuted,
    Rgb Divider,
    Rgb Accent);

/// <summary>
/// 配色表：深色 / 浅色两套底，<c>Tone</c> 把背景往强调色上轻微调色。
/// 注意 Tone 改的是背景色阶，不是窗口透明度（decisions.md D3 禁止分层窗口）。
/// </summary>
public static class ThemePalette
{
    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";

    private static readonly ThemeColors DarkBase = new(
        WindowBackground: Rgb.FromHex("#10151C"),
        CardBackground: Rgb.FromHex("#161E28"),
        TextPrimary: Rgb.FromHex("#E8EEF6"),
        TextSecondary: Rgb.FromHex("#9FB0C5"),
        TextMuted: Rgb.FromHex("#6C7C90"),
        Divider: Rgb.FromHex("#1AFFFFFF"),
        Accent: Rgb.FromHex("#6FA8FF"));

    private static readonly ThemeColors LightBase = new(
        WindowBackground: Rgb.FromHex("#F2F4F8"),
        CardBackground: Rgb.FromHex("#FFFFFF"),
        TextPrimary: Rgb.FromHex("#1B2430"),
        TextSecondary: Rgb.FromHex("#5A6B7D"),
        TextMuted: Rgb.FromHex("#93A1B1"),
        Divider: Rgb.FromHex("#22000000"),
        Accent: Rgb.FromHex("#2F6FE4"));

    public static bool IsLight(string? theme) =>
        string.Equals(theme, LightTheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>按主题名取色；未知主题一律按深色处理（旧文件里可能写着别的东西）。</summary>
    public static ThemeColors For(string? theme, double tone)
    {
        var colors = IsLight(theme) ? LightBase : DarkBase;
        return ApplyTone(colors, tone);
    }

    public static ThemeColors ApplyTone(ThemeColors colors, double tone)
    {
        tone = double.IsNaN(tone) ? 0 : Math.Clamp(tone, 0, 1);
        if (tone == 0)
        {
            return colors;
        }

        // 只有背景参与调色，文字色保持不变，避免对比度被调坏
        var strength = IsLightColors(colors) ? 0.12 : 0.18;
        return colors with
        {
            WindowBackground = colors.WindowBackground.Lerp(colors.Accent, tone * strength),
            CardBackground = colors.CardBackground.Lerp(colors.Accent, tone * strength * 0.6),
        };
    }

    private static bool IsLightColors(ThemeColors colors) => colors.TextPrimary.R < 0x80;
}
