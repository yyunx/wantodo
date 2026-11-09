namespace Wantodo.Core.Tests;

using Wantodo.Core;

public class PlacementGuardTests
{
    // 主屏 0,0 → 1920,1080
    private const double L = 0, T = 0, R = 1920, B = 1080;

    [Fact]
    public void Clamp_LeavesVisibleWindowAlone()
    {
        var (left, top) = PlacementGuard.Clamp(400, 300, 600, 360, L, T, R, B);

        Assert.Equal(400, left);
        Assert.Equal(300, top);
    }

    [Fact]
    public void Clamp_BringsFullyOffscreenWindowBackInside()
    {
        var (left, top) = PlacementGuard.Clamp(5200, 1400, 600, 360, L, T, R, B);

        Assert.Equal(1920 - 600, left);
        Assert.Equal(1080 - 360, top);
    }

    [Fact]
    public void Clamp_PullsBackWindowThatWasOnAnUnpluggedMonitor()
    {
        // 原来在副屏 -2560..0，副屏拔了以后整个都在外面
        var (left, top) = PlacementGuard.Clamp(-2400, 200, 600, 360, L, T, R, B);

        Assert.Equal(0, left);
        Assert.Equal(200, top);
    }

    [Fact]
    public void Clamp_PullsBackWindowBelowTheScreen()
    {
        var (left, top) = PlacementGuard.Clamp(300, 2000, 600, 360, L, T, R, B);

        Assert.Equal(300, left);
        Assert.Equal(1080 - 360, top);
    }

    [Fact]
    public void Clamp_SticksToTopLeftWhenWindowIsWiderThanTheScreen()
    {
        var (left, top) = PlacementGuard.Clamp(500, 100, 3000, 360, L, T, R, B);

        Assert.Equal(0, left);
        Assert.Equal(100, top);
    }

    [Fact]
    public void IsFullyInside_TrueWhenWindowSitsInside()
    {
        Assert.True(PlacementGuard.IsFullyInside(400, 300, 600, 360, L, T, R, B));
    }

    [Fact]
    public void IsFullyInside_FalseWhenWindowStraddlesTheEdge()
    {
        Assert.False(PlacementGuard.IsFullyInside(1800, 300, 600, 360, L, T, R, B));
    }

    [Fact]
    public void IsFullyInside_FalseWhenWindowIsOffscreen()
    {
        Assert.False(PlacementGuard.IsFullyInside(5200, 1400, 600, 360, L, T, R, B));
    }

    [Fact]
    public void Clamp_HandlesMultiMonitorVirtualScreen()
    {
        // 虚拟桌面 -1920,0 → 1920,1080（副屏在左边）
        var (left, top) = PlacementGuard.Clamp(-1500, 100, 600, 360, -1920, T, R, B);

        Assert.Equal(-1500, left);
        Assert.Equal(100, top);
    }
}