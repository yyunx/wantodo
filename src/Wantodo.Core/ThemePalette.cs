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

/// <summary>主题的形式维度（与色板正交）。块 C 先把全部主题压到 <see cref="Flat"/>。</summary>
public enum ThemeForm
{
    Flat,
    Brutal,
    Glass,
    Neon,
    Clay,
    Neumorph,
}

/// <summary>形式参数；纹理键对应 App 层 <c>ThemeStyles.xaml</c> 里的 DrawingBrush。</summary>
public readonly record struct ThemeEffects(
    ThemeForm Form,
    double StrokeWidth,
    double CornerRadius,
    double ShadowOpacity,
    string? StrokeHex,
    string? TextureKey)
{
    public static ThemeEffects Flat { get; } = new(ThemeForm.Flat, 0, 10, 0, null, null);
}

/// <summary>一套主题的语义令牌：10 个基础色 + 6 个卡片/描边语义色 + 2 个数值。</summary>
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
    Rgb Selected,
    Rgb SurfaceCard,
    Rgb OnSurfaceCard,
    Rgb BorderSubtle,
    Rgb BorderActive,
    Rgb AccentSoft,
    Rgb? Glow,
    double CornerRadius,
    int Elevation)
{
    public ThemeForm Form { get; init; } = ThemeForm.Flat;
}

/// <summary>
/// v6 配色表：Tokyo Night Day / Tokyo Night、Catppuccin 四套、霓虹，共 7 套。
/// 旧 id（GitHub 深/浅、Dracula、Nord、樱花、赛博、美漫、System、Custom）由
/// <see cref="MigrateThemeId"/> 映射到新 id，旧 data.json 不会打不开。
/// 正文/次要/弱化在构造时按对比度下限校正（<see cref="ThemeDerivation.EnsureContrast"/>）。
/// </summary>
public static class ThemePalette
{
    public const string LightTheme = "light";
    public const string DarkTheme = "dark";
    public const string LatteTheme = "latte";
    public const string FrappeTheme = "frappe";
    public const string MacchiatoTheme = "macchiato";
    public const string MochaTheme = "mocha";
    public const string NeonTheme = "neon";

    /// <summary>内部调试用，不进 UI；旧库里出现时由 <see cref="MigrateThemeId"/> 映射到深色。</summary>
    public const string CustomTheme = "custom";

    /// <summary>设置页顺序：浅 → 深 → Catppuccin 由浅到深 → 霓虹。</summary>
    public static readonly IReadOnlyList<string> ThemeNames = new[]
    {
        LightTheme, DarkTheme, LatteTheme, FrappeTheme, MacchiatoTheme, MochaTheme, NeonTheme,
    };

    public static string DisplayName(string? theme) => MigrateThemeId(theme) switch
    {
        LightTheme => "浅色",
        LatteTheme => "Catppuccin Latte",
        FrappeTheme => "Catppuccin Frappé",
        MacchiatoTheme => "Catppuccin Macchiato",
        MochaTheme => "Catppuccin Mocha",
        NeonTheme => "霓虹",
        _ => "深色",
    };

    /// <summary>
    /// 旧主题 id → 新 id。删除主题必须走这里，否则旧用户启动会被静默改回深色。
    /// </summary>
    public static string MigrateThemeId(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return DarkTheme;
        }

        var value = theme.Trim();
        return value.ToLowerInvariant() switch
        {
            "light" or "github-light" or "sakura" or "comic" => LightTheme,
            "latte" => LatteTheme,
            "frappe" => FrappeTheme,
            "macchiato" => MacchiatoTheme,
            "mocha" or "dracula" or "catppuccin" => MochaTheme,
            "neon" or "cyber" => NeonTheme,
            "dark" or "github-dark" or "nord" or "system" or "custom" => DarkTheme,
            _ => DarkTheme,
        };
    }

    private static readonly ThemeColors LightBase = Build(
        window: "#E1E2E7", card: "#F7F8FA",
        text: "#1F2335", secondary: "#4B5478", muted: "#848CB5",
        accent: "#2E7DE9", onAccent: "#FFFFFF", divider: "#CFD4E3", elevation: 1);

    private static readonly ThemeColors DarkBase = Build(
        window: "#1A1B26", card: "#24283B",
        text: "#C0CAF5", secondary: "#9AA5CE", muted: "#565F89",
        accent: "#7AA2F7", onAccent: "#16161E", divider: "#3B4261");

    private static readonly ThemeColors LatteBase = Build(
        window: "#EFF1F5", card: "#FFFFFF",
        text: "#4C4F69", secondary: "#6C6F85", muted: "#8C8FA1",
        accent: "#8839EF", onAccent: "#FFFFFF", divider: "#CCD0DA", elevation: 1);

    private static readonly ThemeColors FrappeBase = Build(
        window: "#303446", card: "#292C3C",
        text: "#C6D0F5", secondary: "#A5ADCE", muted: "#838BA7",
        accent: "#CA9EE6", onAccent: "#232634", divider: "#414559");

    private static readonly ThemeColors MacchiatoBase = Build(
        window: "#24273A", card: "#1E2030",
        text: "#CAD3F5", secondary: "#A5ADCB", muted: "#8087A2",
        accent: "#C6A0F6", onAccent: "#181926", divider: "#363A4F");

    private static readonly ThemeColors MochaBase = Build(
        window: "#1E1E2E", card: "#181825",
        text: "#CDD6F4", secondary: "#A6ADC8", muted: "#7F849C",
        accent: "#CBA6F7", onAccent: "#11111B", divider: "#313244");

    private static readonly ThemeColors NeonBase = Build(
        window: "#0B0F14", card: "#11161E",
        text: "#E6F1FF", secondary: "#9AB0C9", muted: "#5B6B85",
        accent: "#FF2E88", onAccent: "#0B0F14", divider: "#1F2C3A", glow: "#22D3EE");

    public static bool IsLight(string? theme) =>
        MigrateThemeId(theme) is LightTheme or LatteTheme;

    public static bool IsLightColors(ThemeColors colors) => colors.TextPrimary.R < 0x80;

    /// <summary>只有深/浅两套允许 Tone 调色；固定色板不跟着滑杆变。</summary>
    public static bool SupportsTone(string? theme) =>
        MigrateThemeId(theme) is DarkTheme or LightTheme;

    public static ThemeEffects EffectsFor(ThemeForm form) => form switch
    {
        _ => ThemeEffects.Flat,
    };

    /// <summary>按主题名取色；未知主题按映射表回退，旧文件不会白屏。</summary>
    public static ThemeColors For(string? theme, double tone) =>
        For(theme, tone, systemIsLight: false, customBackground: null, customAccent: null);

    public static ThemeColors For(
        string? theme, double tone, bool systemIsLight, string? customBackground = null, string? customAccent = null)
    {
        var id = MigrateThemeId(theme);
        if (string.Equals(theme, CustomTheme, StringComparison.OrdinalIgnoreCase))
        {
            id = CustomTheme;
        }

        if (string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase))
        {
            id = systemIsLight ? LightTheme : DarkTheme;
        }

        var colors = id switch
        {
            LightTheme => LightBase,
            LatteTheme => LatteBase,
            FrappeTheme => FrappeBase,
            MacchiatoTheme => MacchiatoBase,
            MochaTheme => MochaBase,
            NeonTheme => NeonBase,
            CustomTheme => ForCustom(customBackground, customAccent),
            _ => DarkBase,
        };

        return SupportsTone(id) ? ApplyTone(colors, tone) : colors;
    }

    /// <summary>内部调试用：只给背景与强调两色，其余由推导补齐（不进 UI）。</summary>
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

        var strength = IsLightColors(colors) ? 0.12 : 0.18;
        return colors with
        {
            WindowBackground = colors.WindowBackground.Lerp(colors.Accent, tone * strength),
            CardBackground = colors.CardBackground.Lerp(colors.Accent, tone * strength * 0.6),
            SurfaceCard = colors.SurfaceCard.Lerp(colors.Accent, tone * strength * 0.6),
            Hover = colors.Hover.Lerp(colors.Accent, tone * strength * 0.5),
            Selected = colors.Selected.Lerp(colors.Accent, tone * strength * 0.5),
        };
    }

    private static ThemeColors Build(
        string window, string card, string text, string secondary, string muted,
        string accent, string onAccent, string divider, string? glow = null, int elevation = 0)
    {
        var bg = Rgb.FromHex(window);
        var cardColor = Rgb.FromHex(card);

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
            Selected: cardColor.Lerp(accentColor, 0.22),
            SurfaceCard: cardColor,
            OnSurfaceCard: textColor,
            BorderSubtle: Rgb.FromHex(divider),
            BorderActive: accentColor,
            AccentSoft: bg.Lerp(accentColor, 0.18),
            Glow: glow is null ? null : Rgb.FromHex(glow),
            CornerRadius: 10,
            Elevation: elevation);
    }

    private static Rgb EnsureBoth(Rgb first, Rgb second, Rgb foreground, double target)
    {
        var againstFirst = ThemeDerivation.EnsureContrast(first, foreground, target);
        return ThemeDerivation.EnsureContrast(second, againstFirst, target);
    }

    /// <summary>把用户输入的颜色规整成 <c>#RRGGBB</c>；空值或格式不对返回 <c>null</c>。</summary>
    public static string? NormalizeHex(string? hex) =>
        TryParse(hex, out var color) ? color.ToHex() : null;

    public static bool TryParseHex(string? hex, out Rgb color) => TryParse(hex, out color);

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
