namespace Wantodo.Core.Tests;

using Wantodo.Core;
using Xunit;

public sealed class ThemeFormTests
{
    [Theory]
    [InlineData("flat", ThemeForm.Flat)]
    [InlineData("brutal", ThemeForm.Brutal)]
    [InlineData("glass", ThemeForm.Glass)]
    [InlineData("neon", ThemeForm.Neon)]
    [InlineData("clay", ThemeForm.Clay)]
    [InlineData("neumorph", ThemeForm.Neumorph)]
    [InlineData("BRUTAL", ThemeForm.Brutal)]
    [InlineData(" glow ", ThemeForm.Flat)]
    [InlineData(null, ThemeForm.Flat)]
    [InlineData("", ThemeForm.Flat)]
    public void ParseForm_MapsKnownIdsAndFallsBackToFlat(string? id, ThemeForm expected)
    {
        Assert.Equal(expected, ThemePalette.ParseForm(id));
    }

    [Theory]
    [InlineData(ThemeForm.Flat)]
    [InlineData(ThemeForm.Brutal)]
    [InlineData(ThemeForm.Glass)]
    [InlineData(ThemeForm.Neon)]
    [InlineData(ThemeForm.Clay)]
    [InlineData(ThemeForm.Neumorph)]
    public void FormId_RoundTrips(ThemeForm form)
    {
        Assert.Equal(form, ThemePalette.ParseForm(ThemePalette.FormId(form)));
    }

    [Fact]
    public void EffectsFor_BrutalHasTwoPixelBlackStrokeAndHardShadow()
    {
        var effects = ThemePalette.EffectsFor(ThemeForm.Brutal);

        Assert.Equal(2, effects.StrokeWidth);
        Assert.Equal(0, effects.CornerRadius);
        Assert.Equal(0.65, effects.ShadowOpacity, 3);
        Assert.Equal(4, effects.ShadowOffset, 3);
        Assert.Equal("#0F1014", effects.StrokeHex);
        Assert.Null(effects.TextureKey);
    }

    [Fact]
    public void EffectsFor_GlassIsTranslucentAndFlatIsPlain()
    {
        var glass = ThemePalette.EffectsFor(ThemeForm.Glass);
        Assert.Equal(0.72, glass.CardAlpha, 3);
        Assert.True(glass.StrokeWidth > 0);

        var flat = ThemePalette.EffectsFor(ThemeForm.Flat);
        Assert.Equal(0, flat.StrokeWidth);
        Assert.Equal(0, flat.ShadowOpacity);
        Assert.Equal(1, flat.CardAlpha);
    }

    [Fact]
    public void Board_FormIdDefaultsToFlatAndNormalizesUnknown()
    {
        var board = Board.CreateDefault();
        Assert.Equal("flat", board.FormId);

        board.FormId = "Brutal";
        board.NormalizePanel();
        Assert.Equal("brutal", board.FormId);

        board.FormId = "wat";
        board.NormalizePanel();
        Assert.Equal("flat", board.FormId);
    }

    [Fact]
    public void Board_OldFileWithoutFormId_LoadsAsFlat()
    {
        var board = Board.CreateDefault();
        board.FormId = null!;
        board.NormalizePanel();

        Assert.Equal("flat", board.FormId);
    }
}
