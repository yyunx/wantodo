using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>0.7.0 的色板/材质/资源键三件套（P0.2）。对应验收 V-02 / V-03 的身份部分。</summary>
public sealed class ThemeIdentityTests
{
    [Theory]
    [InlineData("github-dark", "dark")]
    [InlineData("github-light", "light")]
    [InlineData("nord", "dark")]
    [InlineData("system", "dark")]
    [InlineData("dracula", "mocha")]
    [InlineData("catppuccin", "mocha")]
    [InlineData("sakura", "light")]
    [InlineData("comic", "light")]
    [InlineData("cyber", "neon")]
    [InlineData("SkyNet", "dark")]
    [InlineData("", "dark")]
    [InlineData(null, "dark")]
    [InlineData("MOCHA", "mocha")]
    public void PaletteIds_Parse_MigratesLegacyAndFallsBackToDark(string? input, string expected)
    {
        Assert.Equal(expected, PaletteIds.Parse(input));
    }

    [Fact]
    public void PaletteIds_Parse_KeepsCustom()
    {
        Assert.Equal(PaletteIds.Custom, PaletteIds.Parse("Custom"));
        Assert.False(PaletteIds.IsKnown(PaletteIds.Custom));
        Assert.Equal(7, PaletteIds.Selectable.Count);
    }

    [Theory]
    [InlineData("flat", "liquidGlass")]
    [InlineData("brutal", "liquidGlass")]
    [InlineData("glass", "liquidGlass")]
    [InlineData("neon", "liquidGlass")]
    [InlineData("clay", "liquidGlass")]
    [InlineData("neumorph", "liquidGlass")]
    [InlineData("wat", "liquidGlass")]
    [InlineData(null, "liquidGlass")]
    [InlineData("liquidGlass", "liquidGlass")]
    [InlineData("solid", "solid")]
    public void ThemeIds_Parse_CollapsesAllLegacyFormsIntoGlass(string? input, string expected)
    {
        Assert.Equal(expected, ThemeIds.Parse(input));
    }

    [Fact]
    public void ThemeResourceKeys_AreUniqueAndComplete()
    {
        Assert.Equal(30, ThemeResourceKeys.All.Count);
        Assert.Equal(ThemeResourceKeys.All.Count, ThemeResourceKeys.All.Distinct(StringComparer.Ordinal).Count());

        // 与 0.6.0 ThemeService 实际写过的键一一对应；少一个就是 XAML 会拿到 null。
        foreach (var key in new[]
                 {
                     "WindowBackground", "CardBackground", "TextPrimary", "TextSecondary", "TextMuted",
                     "Divider", "Accent", "OnAccent", "Hover", "Selected",
                     "SurfaceCard", "OnSurfaceCard", "BorderSubtle", "BorderActive", "AccentSoft", "Glow",
                     "ThemeCornerRadiusDouble", "ThemeElevation",
                     "ThemeStrokeBrush", "ThemeStrokeWidth", "ThemeStrokeThickness", "ThemeCornerRadius",
                     "ThemeTextureBrush", "ThemeCardBorderBrush", "ThemeCardBorderThickness",
                     "ThemeCardCornerRadius", "ThemeGlowVisible", "ThemeShadowBrush", "ThemeShadowOffset",
                     "ThemeCardAlpha",
                 })
        {
            Assert.Contains(key, ThemeResourceKeys.All);
        }
    }
}
