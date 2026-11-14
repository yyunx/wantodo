namespace Wantodo.Core;

/// <summary>
/// 对比度计算与自动校正（WCAG 2.x 相对亮度）。
/// 预设色板直接取自各项目，但公开色板里不少「弱化色」在白/深底上达不到 4.5:1，
/// 所以构造 <see cref="ThemeColors"/> 时统一过一遍 <see cref="EnsureContrast"/>；
/// 自定义主题（只选背景 + 强调两色）也靠这里推导其余 8 个令牌。
/// 纯函数，不依赖 WPF，单测直接断言比值。
/// </summary>
public static class ThemeDerivation
{
    public const double TextPrimaryTarget = 7.0;
    public const double TextSecondaryTarget = 4.5;
    public const double TextMutedTarget = 4.5;
    public const double OnAccentTarget = 4.5;

    /// <summary>校正步长：每次把前景往黑或白拉 4%，最多 25 步（≈100%）。</summary>
    private const double Step = 0.04;
    private const int MaxSteps = 25;

    /// <summary>WCAG 相对亮度，0 = 黑，1 = 白。</summary>
    public static double RelativeLuminance(Rgb color)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>对比度比值，1 = 完全相同，21 = 纯黑对纯白。</summary>
    public static double Contrast(Rgb first, Rgb second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// 把 <paramref name="foreground"/> 沿着「离背景更远」的方向往纯黑或纯白推，
    /// 直到与背景的对比度达到 <paramref name="target"/>；已经达标就原样返回。
    /// 方向由前景与背景的亮度关系决定，所以浅色文字不会突然变黑。
    /// </summary>
    public static Rgb EnsureContrast(Rgb background, Rgb foreground, double target)
    {
        if (Contrast(background, foreground) >= target)
        {
            return foreground;
        }

        var towardWhite = RelativeLuminance(foreground) >= RelativeLuminance(background);
        var target0 = towardWhite ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);

        var current = foreground;
        for (var i = 1; i <= MaxSteps; i++)
        {
            current = foreground.Lerp(target0, i * Step);
            if (Contrast(background, current) >= target)
            {
                return current;
            }
        }

        return target0;
    }

    /// <summary>背景是否偏亮（用于决定自定义主题的推导方向）。</summary>
    public static bool IsLightBackground(Rgb background) => RelativeLuminance(background) > 0.35;

    /// <summary>
    /// 自定义主题：给背景 + 强调两色，推出 10 个令牌。
    /// 规则：卡片 = 背景按底色方向挪一档；正文取反色族；强调上文字黑白取优；
    /// 全部再过 <see cref="EnsureContrast"/>，保证阈值成立。
    /// </summary>
    public static ThemeColors DeriveFrom(Rgb background, Rgb accent)
    {
        var light = IsLightBackground(background);
        var text = light ? new Rgb(0x1F, 0x23, 0x28) : new Rgb(0xE6, 0xED, 0xF3);
        var card = light ? background.Lerp(new Rgb(255, 255, 255), 0.55) : background.Lerp(new Rgb(0, 0, 0), 0.09);

        text = EnsureContrast(background, text, TextPrimaryTarget);
        text = EnsureContrast(card, text, TextPrimaryTarget);

        var secondary = EnsureContrast(card, text.Lerp(background, 0.30), TextSecondaryTarget);
        secondary = EnsureContrast(background, secondary, TextSecondaryTarget);

        var muted = EnsureContrast(card, text.Lerp(background, 0.50), TextMutedTarget);
        muted = EnsureContrast(background, muted, TextMutedTarget);

        // 强调色太浅/太深时先自我校正一档，避免和背景糊在一起
        var accentFixed = EnsureContrast(background, accent, 3.0);
        var onAccent = PickOnAccent(accentFixed);

        return new ThemeColors(
            WindowBackground: background,
            CardBackground: card,
            TextPrimary: text,
            TextSecondary: secondary,
            TextMuted: muted,
            Divider: background.Lerp(text, 0.16),
            Accent: accentFixed,
            OnAccent: onAccent,
            Hover: card.Lerp(text, 0.10),
            Selected: card.Lerp(accentFixed, 0.22));
    }

    private static Rgb PickOnAccent(Rgb accent)
    {
        var white = new Rgb(255, 255, 255);
        var black = new Rgb(0, 0, 0);
        var best = Contrast(accent, white) >= Contrast(accent, black) ? white : black;
        return EnsureContrast(accent, best, OnAccentTarget);
    }
}
