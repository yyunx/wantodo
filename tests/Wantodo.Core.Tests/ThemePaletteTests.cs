namespace Wantodo.Core.Tests;

using Wantodo.Core;

public class ThemePaletteTests
{
    [Fact]
    public void For_DefaultsToDarkForUnknownTheme()
    {
        var colors = ThemePalette.For("nonsense", 0);

        Assert.Equal(Rgb.FromHex("#0D1117"), colors.WindowBackground);
    }

    [Fact]
    public void For_ReturnsLightPaletteForLightTheme()
    {
        var colors = ThemePalette.For("Light", 0);

        Assert.Equal(Rgb.FromHex("#F6F8FA"), colors.CardBackground);
        Assert.True(colors.TextPrimary.R < 0x80, "浅色主题的文字应该偏深");
    }

    [Fact]
    public void For_ThemeNameIsCaseInsensitive()
    {
        Assert.Equal(ThemePalette.For("Light", 0), ThemePalette.For("light", 0));
    }

    [Fact]
    public void ApplyTone_ZeroKeepsBaseColors()
    {
        var baseColors = ThemePalette.For("Dark", 0);

        var toned = ThemePalette.ApplyTone(baseColors, 0);

        Assert.Equal(baseColors, toned);
    }

    [Fact]
    public void ApplyTone_TintsBackgroundButKeepsTextReadable()
    {
        var baseColors = ThemePalette.For("Dark", 0);
        var toned = ThemePalette.ApplyTone(baseColors, 1);

        Assert.NotEqual(baseColors.WindowBackground, toned.WindowBackground);
        Assert.Equal(baseColors.TextPrimary, toned.TextPrimary);
        Assert.Equal(baseColors.Accent, toned.Accent);
    }

    [Fact]
    public void ApplyTone_ClampsOutOfRangeAndIgnoresNaN()
    {
        var baseColors = ThemePalette.For("Dark", 0);

        Assert.Equal(ThemePalette.ApplyTone(baseColors, 1), ThemePalette.ApplyTone(baseColors, 5));
        Assert.Equal(baseColors, ThemePalette.ApplyTone(baseColors, double.NaN));
    }

    [Fact]
    public void Rgb_FromHex_AcceptsAlphaAndHashlessForms()
    {
        Assert.Equal(new Rgb(0x10, 0x15, 0x1C), Rgb.FromHex("#10151C"));
        Assert.Equal(new Rgb(0x10, 0x15, 0x1C), Rgb.FromHex("10151C"));
        Assert.Equal(new Rgb(0x10, 0x15, 0x1C), Rgb.FromHex("#FF10151C"));
    }

    [Fact]
    public void Rgb_Lerp_ClampsToEndpoints()
    {
        var a = new Rgb(0, 0, 0);
        var b = new Rgb(100, 200, 50);

        Assert.Equal(a, a.Lerp(b, 0));
        Assert.Equal(b, a.Lerp(b, 1));
        Assert.Equal(new Rgb(50, 100, 25), a.Lerp(b, 0.5));
    }
}
