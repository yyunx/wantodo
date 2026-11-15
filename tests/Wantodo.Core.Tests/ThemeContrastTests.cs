namespace Wantodo.Core.Tests;

using Wantodo.Core;

/// <summary>
/// 7 套预设的对比度下限。阈值写死在这里，改配色表就会撞红：
/// 正文 ≥7、次要/弱化 ≥4.5、强调色上的文字 ≥4.5、卡片上的正文 ≥7。
/// </summary>
public class ThemeContrastTests
{
    public static TheoryData<string> Presets => new()
    {
        ThemePalette.LightTheme,
        ThemePalette.DarkTheme,
        ThemePalette.LatteTheme,
        ThemePalette.FrappeTheme,
        ThemePalette.MacchiatoTheme,
        ThemePalette.MochaTheme,
        ThemePalette.NeonTheme,
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void Preset_MeetsContrastTargets(string theme)
    {
        var colors = ThemePalette.For(theme, 0);

        Assert.True(
            ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget,
            $"{theme} 正文对窗口底 {ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary):F2}");
        Assert.True(
            ThemeDerivation.Contrast(colors.CardBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget,
            $"{theme} 正文对卡片底 {ThemeDerivation.Contrast(colors.CardBackground, colors.TextPrimary):F2}");
        Assert.True(
            ThemeDerivation.Contrast(colors.WindowBackground, colors.TextSecondary) >= ThemeDerivation.TextSecondaryTarget,
            $"{theme} 次要 {ThemeDerivation.Contrast(colors.WindowBackground, colors.TextSecondary):F2}");
        Assert.True(
            ThemeDerivation.Contrast(colors.CardBackground, colors.TextMuted) >= ThemeDerivation.TextMutedTarget,
            $"{theme} 弱化 {ThemeDerivation.Contrast(colors.CardBackground, colors.TextMuted):F2}");
        Assert.True(
            ThemeDerivation.Contrast(colors.Accent, colors.OnAccent) >= ThemeDerivation.OnAccentTarget,
            $"{theme} 强调上文字 {ThemeDerivation.Contrast(colors.Accent, colors.OnAccent):F2}");
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void Preset_CardSurfaceMatchesSemanticTokens(string theme)
    {
        var colors = ThemePalette.For(theme, 0);

        Assert.Equal(colors.CardBackground, colors.SurfaceCard);
        Assert.Equal(colors.TextPrimary, colors.OnSurfaceCard);
        Assert.Equal(colors.Accent, colors.BorderActive);
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void Preset_HoverAndSelectedAreDistinctFromCard(string theme)
    {
        var colors = ThemePalette.For(theme, 0);

        Assert.NotEqual(colors.CardBackground, colors.Hover);
        Assert.NotEqual(colors.CardBackground, colors.Selected);
        Assert.NotEqual(colors.Hover, colors.Selected);
    }

    [Fact]
    public void DisplayName_IsFriendly()
    {
        Assert.Equal("浅色", ThemePalette.DisplayName(ThemePalette.LightTheme));
        Assert.Equal("深色", ThemePalette.DisplayName(ThemePalette.DarkTheme));
        Assert.Equal("Catppuccin Mocha", ThemePalette.DisplayName(ThemePalette.MochaTheme));
        Assert.Equal("霓虹", ThemePalette.DisplayName(ThemePalette.NeonTheme));
    }

    [Theory]
    [InlineData("github-dark", ThemePalette.DarkTheme)]
    [InlineData("github-light", ThemePalette.LightTheme)]
    [InlineData("nord", ThemePalette.DarkTheme)]
    [InlineData("dracula", ThemePalette.MochaTheme)]
    [InlineData("catppuccin", ThemePalette.MochaTheme)]
    [InlineData("sakura", ThemePalette.LightTheme)]
    [InlineData("comic", ThemePalette.LightTheme)]
    [InlineData("cyber", ThemePalette.NeonTheme)]
    [InlineData("custom", ThemePalette.DarkTheme)]
    [InlineData("System", ThemePalette.DarkTheme)]
    [InlineData("", ThemePalette.DarkTheme)]
    [InlineData("what-else", ThemePalette.DarkTheme)]
    public void MigrateThemeId_MapsLegacyIds(string legacy, string expected)
    {
        Assert.Equal(expected, ThemePalette.MigrateThemeId(legacy));
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void MigratedThemes_AreValid(string theme)
    {
        Assert.Equal(theme, ThemePalette.MigrateThemeId(theme));
        Assert.Contains(theme, ThemePalette.ThemeNames);
    }

    [Fact]
    public void For_PresetNamesAreCaseInsensitive()
    {
        Assert.Equal(ThemePalette.For("mocha", 0), ThemePalette.For("MOCHA", 0));
        Assert.Equal(ThemePalette.For("dark", 0), ThemePalette.For("GitHub-Dark", 0));
    }

    [Fact]
    public void For_FixedPalettesIgnoreTone()
    {
        foreach (var theme in new[] { ThemePalette.LatteTheme, ThemePalette.FrappeTheme, ThemePalette.MochaTheme, ThemePalette.NeonTheme })
        {
            Assert.Equal(ThemePalette.For(theme, 0), ThemePalette.For(theme, 1));
            Assert.False(ThemePalette.SupportsTone(theme), $"{theme} 不应该跟着色调滑杆变");
        }
    }

    [Fact]
    public void For_LegacySystemFollowsFlag()
    {
        var dark = ThemePalette.For("System", 0, systemIsLight: false);
        var light = ThemePalette.For("System", 0, systemIsLight: true);

        Assert.Equal(ThemePalette.For(ThemePalette.DarkTheme, 0), dark);
        Assert.Equal(ThemePalette.For(ThemePalette.LightTheme, 0), light);
    }

    [Fact]
    public void ForCustom_DerivesReadablePaletteFrom2Colors()
    {
        var colors = ThemePalette.ForCustom("#1B2735", "#E05A7A");

        Assert.True(ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget);
        Assert.True(ThemeDerivation.Contrast(colors.CardBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget);
        Assert.True(ThemeDerivation.Contrast(colors.Accent, colors.OnAccent) >= ThemeDerivation.OnAccentTarget);
        Assert.Equal(Rgb.FromHex("#E05A7A"), colors.Accent);
    }

    [Fact]
    public void ForCustom_LightBackgroundGetsDarkText()
    {
        var colors = ThemePalette.ForCustom("#FDF6E3", "#B58900");

        Assert.True(colors.TextPrimary.R < 0x80, "浅底上的正文应该偏深");
        Assert.True(ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget);
    }

    [Fact]
    public void ForCustom_InvalidHexFallsBackToDark()
    {
        Assert.Equal(ThemePalette.For(ThemePalette.DarkTheme, 0), ThemePalette.ForCustom("oops", "#123456"));
        Assert.Equal(ThemePalette.For(ThemePalette.DarkTheme, 0), ThemePalette.ForCustom(null, null));
    }

    [Fact]
    public void ForCustom_WhiteOnWhite_StillReadable()
    {
        var colors = ThemePalette.ForCustom("#FFFFFF", "#FFFFFF");

        Assert.True(ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget);
        Assert.True(ThemeDerivation.Contrast(colors.CardBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget);
        Assert.True(ThemeDerivation.Contrast(colors.WindowBackground, colors.TextSecondary) >= ThemeDerivation.TextSecondaryTarget);
        Assert.True(ThemeDerivation.Contrast(colors.WindowBackground, colors.TextMuted) >= ThemeDerivation.TextMutedTarget);
        Assert.True(ThemeDerivation.Contrast(colors.Accent, colors.OnAccent) >= ThemeDerivation.OnAccentTarget);
        Assert.True(colors.TextPrimary.R < 0x80, "白底上必须推成深色文字");
    }

    [Fact]
    public void ForCustom_TwoHundredRandomPairs_KeepInvariants()
    {
        var random = new Random(20261114);
        var started = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 200; i++)
        {
            var background = $"#{random.Next(256):X2}{random.Next(256):X2}{random.Next(256):X2}";
            var accent = $"#{random.Next(256):X2}{random.Next(256):X2}{random.Next(256):X2}";

            var colors = ThemePalette.ForCustom(background, accent);

            Assert.True(
                ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget,
                $"背景 {background} 上正文 {ThemeDerivation.Contrast(colors.WindowBackground, colors.TextPrimary):F2}");
            Assert.True(
                ThemeDerivation.Contrast(colors.CardBackground, colors.TextPrimary) >= ThemeDerivation.TextPrimaryTarget,
                $"背景 {background} 上卡片正文 {ThemeDerivation.Contrast(colors.CardBackground, colors.TextPrimary):F2}");
            Assert.True(
                ThemeDerivation.Contrast(colors.WindowBackground, colors.TextSecondary) >= ThemeDerivation.TextSecondaryTarget,
                $"背景 {background} 上次要 {ThemeDerivation.Contrast(colors.WindowBackground, colors.TextSecondary):F2}");
            Assert.True(
                ThemeDerivation.Contrast(colors.WindowBackground, colors.TextMuted) >= ThemeDerivation.TextMutedTarget,
                $"背景 {background} 上弱化 {ThemeDerivation.Contrast(colors.WindowBackground, colors.TextMuted):F2}");
            Assert.True(
                ThemeDerivation.Contrast(colors.CardBackground, colors.TextMuted) >= ThemeDerivation.TextMutedTarget,
                $"背景 {background} 上卡片弱化 {ThemeDerivation.Contrast(colors.CardBackground, colors.TextMuted):F2}");
            Assert.True(
                ThemeDerivation.Contrast(colors.Accent, colors.OnAccent) >= ThemeDerivation.OnAccentTarget,
                $"强调 {accent} 上的文字 {ThemeDerivation.Contrast(colors.Accent, colors.OnAccent):F2}");
        }

        started.Stop();
        Assert.True(
            started.Elapsed.TotalMilliseconds / 200 < 5,
            $"单组推导应小于 5 ms，实测平均 {started.Elapsed.TotalMilliseconds / 200:F2} ms");
    }

    [Fact]
    public void EnsureContrast_LeavesGoodPairAlone()
    {
        var bg = Rgb.FromHex("#10151C");
        var fg = Rgb.FromHex("#FFFFFF");

        Assert.Equal(fg, ThemeDerivation.EnsureContrast(bg, fg, 7));
    }

    [Fact]
    public void EnsureContrast_PushesWeakPairToTarget()
    {
        var bg = Rgb.FromHex("#F2F4F8");
        var weak = Rgb.FromHex("#D0D4DA");

        var fixedColor = ThemeDerivation.EnsureContrast(bg, weak, 4.5);

        Assert.True(ThemeDerivation.Contrast(bg, fixedColor) >= 4.5);
        Assert.True(fixedColor.R < weak.R, "浅底上的弱文字应该被压深");
    }

    [Fact]
    public void Contrast_BlackOnWhiteIs21()
    {
        Assert.Equal(21, ThemeDerivation.Contrast(new Rgb(0, 0, 0), new Rgb(255, 255, 255)), 3);
        Assert.Equal(1, ThemeDerivation.Contrast(new Rgb(10, 20, 30), new Rgb(10, 20, 30)), 3);
    }

    [Fact]
    public void LightThemes_AreLight()
    {
        Assert.True(ThemePalette.IsLight(ThemePalette.LightTheme));
        Assert.True(ThemePalette.IsLight(ThemePalette.LatteTheme));
        Assert.False(ThemePalette.IsLight(ThemePalette.DarkTheme));
        Assert.False(ThemePalette.IsLight(ThemePalette.NeonTheme));
    }

    [Fact]
    public void Neon_HasGlowAndOthersDoNot()
    {
        Assert.NotNull(ThemePalette.For(ThemePalette.NeonTheme, 0).Glow);
        Assert.Null(ThemePalette.For(ThemePalette.DarkTheme, 0).Glow);
    }
}
