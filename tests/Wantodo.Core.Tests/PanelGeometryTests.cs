using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class PanelGeometryTests
{
    private static readonly PanelRect Work1366 = new(0, 0, 1366, 728);

    private static readonly PanelRect Work2560 = new(0, 0, 2560, 1560);

    [Theory]
    [InlineData(1366, 728, 1024.5, 546)]
    [InlineData(2560, 1560, 1920, 1170)]
    public void ExpandedSize_DefaultRatio_IsThreeQuartersOfWorkArea(
        double workWidth, double workHeight, double expectedWidth, double expectedHeight)
    {
        var work = new PanelRect(0, 0, workWidth, workHeight);

        var size = PanelGeometry.ExpandedSize(work, 0.75, 0.75);

        Assert.Equal(expectedWidth, size.Width, 3);
        Assert.Equal(expectedHeight, size.Height, 3);
    }

    [Fact]
    public void ClampRatio_BadValues_FallBackToDefault()
    {
        Assert.Equal(0.75, PanelGeometry.ClampRatio(double.NaN), 3);
        Assert.Equal(0.75, PanelGeometry.ClampRatio(double.PositiveInfinity), 3);
        Assert.Equal(0.9, PanelGeometry.ClampRatio(1.5), 3);
        Assert.Equal(0.2, PanelGeometry.ClampRatio(0.1), 3);
        Assert.Equal(0.6, PanelGeometry.ClampRatio(0.6), 3);
    }

    [Fact]
    public void ExpandedSize_ClampsToMinimumAndNinetyPercent()
    {
        var tiny = new PanelRect(0, 0, 500, 300);
        var tinySize = PanelGeometry.ExpandedSize(tiny, 0.75, 0.75);
        Assert.Equal(450, tinySize.Width, 3); // 375 想抬到最小 480，但 90% 工作区只有 450，取 450
        Assert.Equal(240, tinySize.Height, 3); // 225 抬到最小 240
        Assert.True(tinySize.Width <= tiny.Width * PanelGeometry.MaxWorkAreaFraction + 0.001);

        var big = new PanelRect(0, 0, 2000, 1000);
        var bigSize = PanelGeometry.ExpandedSize(big, 1.0, 1.0);
        Assert.Equal(1800, bigSize.Width, 3);
        Assert.Equal(900, bigSize.Height, 3);
    }

    [Fact]
    public void HitTest_EdgesAndReservedTop()
    {
        var window = new PanelRect(100, 0, 800, 600);

        Assert.Equal(PanelEdge.Left, PanelGeometry.HitTest(window, 102, 300));
        Assert.Equal(PanelEdge.Right, PanelGeometry.HitTest(window, 898, 300));
        Assert.Equal(PanelEdge.Bottom, PanelGeometry.HitTest(window, 500, 598));
        Assert.Equal(PanelEdge.BottomRight, PanelGeometry.HitTest(window, 898, 598));
        Assert.Equal(PanelEdge.BottomLeft, PanelGeometry.HitTest(window, 102, 598));
        Assert.Equal(PanelEdge.None, PanelGeometry.HitTest(window, 500, 300));
        Assert.Equal(PanelEdge.None, PanelGeometry.HitTest(window, 102, 5)); // 顶边 12 DIP 留给把手
        Assert.Equal(PanelEdge.None, PanelGeometry.HitTest(window, 50, 300)); // 窗口外
    }

    [Fact]
    public void Resize_LeftEdge_MovesLeftAndKeepsRightAnchored()
    {
        var start = new PanelRect(100, 0, 800, 600);

        var resized = PanelGeometry.Resize(start, PanelEdge.Left, 40, 0, Work1366);

        Assert.Equal(140, resized.Left, 3);
        Assert.Equal(760, resized.Width, 3);
        Assert.Equal(900, resized.Right, 3);
        Assert.Equal(600, resized.Height, 3);
    }

    [Fact]
    public void Resize_BottomEdge_ClampsToMinHeightAndSavedRatioMatchesHeight()
    {
        var start = new PanelRect(100, 0, 800, 600);

        var resized = PanelGeometry.Resize(start, PanelEdge.Bottom, 0, -1000, Work1366);

        // 240 DIP 下限优先于 0.2×728=145.6；存回的比例就是 240/728，下次展开不会跳。
        Assert.Equal(PanelGeometry.MinHeight, resized.Height, 3);
        Assert.Equal(800, resized.Width, 3);

        var (_, heightRatio) = PanelGeometry.RatiosFromSize(
            Work1366,
            new PanelSize(resized.Width, resized.Height));
        Assert.Equal(PanelGeometry.MinHeight / Work1366.Height, heightRatio, 3);
    }

    [Fact]
    public void Resize_BottomEdge_SequenceIsReversible()
    {
        var work = new PanelRect(0, 0, 1366, 2000);
        var start = new PanelRect(100, 0, 800, 1000);

        var down = PanelGeometry.Resize(start, PanelEdge.Bottom, 0, -500, work);
        Assert.Equal(500, down.Height, 3);

        var back = PanelGeometry.Resize(down, PanelEdge.Bottom, 0, 500, work);
        Assert.Equal(1000, back.Height, 3);
    }

    [Fact]
    public void ClampHeight_BelowMinimum_UsesMinHeight()
    {
        Assert.Equal(PanelGeometry.MinHeight, PanelGeometry.ClampHeight(Work1366, 100), 3);
        Assert.Equal(PanelGeometry.MinHeight, PanelGeometry.ClampHeight(Work1366, -50), 3);
        Assert.Equal(PanelGeometry.MinHeight, PanelGeometry.ClampHeight(Work1366, 0), 3);
    }

    [Fact]
    public void ClampHeight_TallerThanNinetyPercent_UsesWorkAreaCap()
    {
        double cap = Work1366.Height * PanelGeometry.MaxWorkAreaFraction;

        Assert.Equal(cap, PanelGeometry.ClampHeight(Work1366, 5000), 3);
        Assert.Equal(cap, PanelGeometry.ClampHeight(Work1366, double.PositiveInfinity), 3);
    }

    [Fact]
    public void HeightRatio_RoundTripsThroughExpandedSize()
    {
        var work = new PanelRect(0, 0, 2560, 1560);

        foreach (double height in new[] { 240, 500, 1000, 1400 })
        {
            double ratio = PanelGeometry.HeightRatio(work, height);
            double restored = PanelGeometry.ExpandedSize(work, 0.75, ratio).Height;
            Assert.InRange(restored, height - 0.5, height + 0.5);
        }
    }

    [Fact]
    public void RatiosFromSize_RoundTripsAndClamps()
    {
        var size = new PanelSize(1024.5, 546);

        var (widthRatio, heightRatio) = PanelGeometry.RatiosFromSize(Work1366, size);

        Assert.Equal(0.75, widthRatio, 3);
        Assert.Equal(0.75, heightRatio, 3);

        var oversized = PanelGeometry.RatiosFromSize(Work1366, new PanelSize(2000, 2000));
        Assert.Equal(0.9, oversized.WidthRatio, 3);
        Assert.Equal(0.9, oversized.HeightRatio, 3);
    }
}
