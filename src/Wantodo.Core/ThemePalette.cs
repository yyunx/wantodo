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

/// <summary>一套主题下的 10 个令牌。</summary>
public sealed record ThemeColors(
    Rgb WindowBackground,
    Rgb CardBackground,
    Rgb TextPrimary,
    Rgb TextSecondary,
    Rgb TextMuted,
    Rgb Divider,
    Rgb Accent,
    Rgb OnAccent,
    Rgb Hover,
    Rgb Selected);

/// <summary>
/// 配色表：GitHub 深/浅色、Dracula、Nord、Catppuccin 五套预设，外加跟随系统与自定义。
/// 预设色取自各项目公开色板；正文/次要/弱化三档在构造时按对比度下限校正
/// （<see cref="ThemeDerivation.EnsureContrast"/>），所以最终值可能比原色板略亮/略暗。
/// <c>Tone</c> 只对 GitHub 深/浅色与自定义生效（decisions.md D3：不透明调色，不是窗口透明度）。
/// </summary>
public static class ThemePalette
{
    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";
    public const string SystemTheme = "System";
    public const string DraculaTheme = "Dracula";
    public const string NordTheme = "Nord";
    public const string CatppuccinTheme = "Catppuccin";
    public const string CustomTheme = "Custom";

    /// <summary>设置页下拉里的顺序：跟随系统在最前，其余按深→浅排。</summary>
    public static readonly IReadOnlyList<string> ThemeNames = new[]
    {
        SystemTheme, DarkTheme, LightTheme, DraculaTheme, NordTheme, CatppuccinTheme,
    };

    private static readonly ThemeColors DarkBase = Build(
        window: "#0D1117", card: "#161B22",
        text: "#E6EDF3", secondary: "#B1BAC4", muted: "#7D8590",
        accent: "#2F81F7", onAccent: "#0D1117", divider: "#30363D");

    private static readonly ThemeColors LightBase = Build(
        window: "#FFFFFF", card: "#F6F8FA",
        text: "#1F2328", secondary: "#4D5560", muted: "#59636E",
        accent: "#0969DA", onAccent: "#FFFFFF", divider: "#D1D9E0");

    private static readonly ThemeColors DraculaBase = Build(
        window: "#282A36", card: "#21222C",
        text: "#F8F8F2", secondary: "#D0D2E4", muted: "#6272A4",
        accent: "#BD93F9", onAccent: "#282A36", divider: "#44475A");

    private static readonly ThemeColors NordBase = Build(
        window: "#2E3440", card: "#3B4252",
        text: "#ECEFF4", secondary: "#D8DEE9", muted: "#81A1C1",
        accent: "#88C0D0", onAccent: "#2E3440", divider: "#434C5E");

    private static readonly ThemeColors CatppuccinBase = Build(
        window: "#1E1E2E", card: "#313244",
        text: "#CDD6F4", secondary: "#BAC2DE", muted: "#A6ADC8",
        accent: "#89B4FA", onAccent: "#1E1E2E", divider: "#45475A");

    /// <summary>主题名 → 是否浅色底（自定义按背景亮度判断）。</summary>
    public static bool IsLight(string? theme) =>
        theme is not null && string.Equals(theme, LightTheme, StringComparison.OrdinalIgnoreCase);

    public static bool IsLightColors(ThemeColors colors) => colors.TextPrimary.R < 0x80;

    /// <summary>只有这几套支持 <c>Tone</c> 调色；固定色板不跟着滑杆变。</summary>
    public static bool SupportsTone(string? theme) =>
        theme is null
        || string.Equals(theme, DarkTheme, StringComparison.OrdinalIgnoreCase)
        || string.Equals(theme, LightTheme, StringComparison.OrdinalIgnoreCase)
        || string.Equals(theme, CustomTheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>按主题名取色；未知主题一律按深色处理（旧文件里可能写着别的东西）。</summary>
    public static ThemeColors For(string? theme, double tone) =>
        For(theme, tone, systemIsLight: false, customBackground: null, customAccent: null);

    /// <summary>
    /// <paramref name="systemIsLight"/> 只在 <c>System</c> 时有意义：
    /// App 层读系统「应用主题」后传进来，Core 不碰注册表。
    /// </summary>
    public static ThemeColors For(
        string? theme, double tone, bool systemIsLight, string? customBackground = null, string? customAccent = null)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return ApplyTone(DarkBase, tone);
        }

        if (string.Equals(theme, SystemTheme, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyTone(systemIsLight ? LightBase : DarkBase, tone);
        }

        if (string.Equals(theme, CustomTheme, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyTone(ForCustom(customBackground, customAccent), tone);
        }

        if (string.Equals(theme, DraculaTheme, StringComparison.OrdinalIgnoreCase))
        {
            return DraculaBase;
        }

        if (string.Equals(theme, NordTheme, StringComparison.OrdinalIgnoreCase))
        {
            return NordBase;
        }

        if (string.Equals(theme, CatppuccinTheme, StringComparison.OrdinalIgnoreCase))
        {
            return CatppuccinBase;
        }

        return ApplyTone(IsLight(theme) ? LightBase : DarkBase, tone);
    }

    /// <summary>
    /// 自定义主题：只让用户选背景与强调两色，其余由 <see cref="ThemeDerivation"/> 推导。
    /// 解析失败或为空时退回深色底，避免把窗口变成不可读的随机颜色。
    /// </summary>
    public static ThemeColors ForCustom(string? backgroundHex, string? accentHex)
    {
        if (!TryParse(backgroundHex, out var background) || !TryParse(accentHex, out var accent))
        {
            return DarkBase;
        }

        return ThemeDerivation.DeriveFrom(background, accent);
    }

    public static ThemeColors ApplyTone(ThemeColors colors, double tone)
    {
        tone = double.IsNaN(tone) ? 0 : Math.Clamp(tone, 0, 1);
        if (tone == 0)
        {
            return colors;
        }

        // 只有背景参与调色，文字与强调色保持不变，避免对比度被调坏
        var strength = IsLightColors(colors) ? 0.12 : 0.18;
        return colors with
        {
            WindowBackground = colors.WindowBackground.Lerp(colors.Accent, tone * strength),
            CardBackground = colors.CardBackground.Lerp(colors.Accent, tone * strength * 0.6),
            Hover = colors.Hover.Lerp(colors.Accent, tone * strength * 0.5),
            Selected = colors.Selected.Lerp(colors.Accent, tone * strength * 0.5),
        };
    }

    private static ThemeColors Build(
        string window, string card, string text, string secondary, string muted,
        string accent, string onAccent, string divider)
    {
        var bg = Rgb.FromHex(window);
        var cardColor = Rgb.FromHex(card);

        // 文字三档对「窗口底」与「卡片底」都要达标，取两者里更严的结果
        var textColor = EnsureBoth(bg, cardColor, Rgb.FromHex(text), ThemeDerivation.TextPrimaryTarget);
        var secondaryColor = EnsureBoth(bg, cardColor, Rgb.FromHex(secondary), ThemeDerivation.TextSecondaryTarget);
        var mutedColor = EnsureBoth(bg, cardColor, Rgb.FromHex(muted), ThemeDerivation.TextMutedTarget);
        var accentColor = Rgb.FromHex(accent);
        var onAccentColor = ThemeDerivation.EnsureContrast(accentColor, Rgb.FromHex(onAccent), ThemeDerivation.OnAccentTarget);

        return new ThemeColors(
            WindowBackground: bg,
            CardBackground: cardColor,
            TextPrimary: textColor,
            TextSecondary: secondaryColor,
            TextMuted: mutedColor,
            Divider: Rgb.FromHex(divider),
            Accent: accentColor,
            OnAccent: onAccentColor,
            Hover: cardColor.Lerp(textColor, 0.10),
            Selected: cardColor.Lerp(accentColor, 0.22));
    }

    private static Rgb EnsureBoth(Rgb first, Rgb second, Rgb foreground, double target)
    {
        var againstFirst = ThemeDerivation.EnsureContrast(first, foreground, target);
        return ThemeDerivation.EnsureContrast(second, againstFirst, target);
    }

    private static bool TryParse(string? hex, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = Rgb.FromHex(hex);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException)
        {
            return false;
        }
    }
}
