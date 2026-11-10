using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class LayoutMetricsTests
{
    [Theory]
    [InlineData(1, 304)]
    [InlineData(2, 592)]
    [InlineData(3, 880)]
    [InlineData(4, 1168)]
    public void ComputePreferredWidth_StraightLineOverPanes(int panes, double expected)
    {
        Assert.Equal(expected, LayoutMetrics.ComputePreferredWidth(panes), 3);
    }

    [Theory]
    [InlineData(1, 214)]
    [InlineData(2, 412)]
    [InlineData(3, 610)]
    [InlineData(4, 808)]
    public void ComputeMinWidth_StraightLineOverPanes(int panes, double expected)
    {
        Assert.Equal(expected, LayoutMetrics.ComputeMinWidth(panes), 3);
    }

    [Fact]
    public void FitTo_KeepsWidthWhenItAlreadyFits()
    {
        double width = LayoutMetrics.ComputePreferredWidth(3);
        Assert.Equal(width, LayoutMetrics.FitTo(width, 3, 1706), 3);
    }

    [Fact]
    public void FitTo_GrowsToMinimumWhenRequestedIsTooNarrow()
    {
        double min = LayoutMetrics.ComputeMinWidth(3);
        Assert.Equal(min, LayoutMetrics.FitTo(300, 3, 1706), 3);
    }

    [Fact]
    public void FitTo_ClampsToWorkArea()
    {
        Assert.Equal(1706, LayoutMetrics.FitTo(5000, 3, 1706), 3);
    }

    [Fact]
    public void FitTo_WorkAreaNarrowerThanMinimum_KeepsMinimum()
    {
        double min = LayoutMetrics.ComputeMinWidth(4);
        Assert.Equal(min, LayoutMetrics.FitTo(500, 4, 600), 3);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-100, 1)]
    [InlineData(304, 1)]
    [InlineData(412, 2)]
    [InlineData(592, 2)]
    [InlineData(880, 4)]
    [InlineData(2000, 10)]
    public void ComputeMaxPanes_CountsWhatFitsAtMinimumWidth(double available, int expected)
    {
        Assert.Equal(expected, LayoutMetrics.ComputeMaxPanes(available));
    }

    [Fact]
    public void SizeModes_DefaultIsAutoFit()
    {
        Assert.Equal(SizeModes.AutoFit, new WindowPlacement().SizeMode);
    }

    [Theory]
    [InlineData("AutoFit", "AutoFit")]
    [InlineData("autofit", "AutoFit")]
    [InlineData("Manual", "Manual")]
    [InlineData("manual", "Manual")]
    [InlineData(null, "AutoFit")]
    [InlineData("", "AutoFit")]
    [InlineData("乱写", "AutoFit")]
    public void SizeModes_NormalizeAcceptsSloppyValues(string? input, string expected)
    {
        Assert.Equal(expected, SizeModes.Normalize(input));
    }
}
