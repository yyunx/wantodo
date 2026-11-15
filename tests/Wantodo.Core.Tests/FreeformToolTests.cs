using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>0.7.0 P1.1：工具常量表与缩放规则（Core 纯逻辑，必须先红后绿）。</summary>
public sealed class FreeformToolTests
{
    [Fact]
    public void Catalog_HasTenTools_WithUniqueIdsAndNames()
    {
        Assert.Equal(10, FreeformToolCatalog.Count);
        Assert.Equal(10, FreeformToolCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(10, FreeformToolCatalog.All.Select(t => t.Name).Distinct().Count());
        Assert.Equal(10, FreeformToolCatalog.All.Select(t => t.Tool).Distinct().Count());

        // 需求原文的十个工具一个都不能缺
        foreach (var tool in Enum.GetValues<FreeformTool>())
        {
            Assert.Contains(FreeformToolCatalog.All, t => t.Tool == tool);
        }
    }

    [Fact]
    public void Catalog_Shortcuts_MatchPlanAndAreUnique()
    {
        var withShortcut = FreeformToolCatalog.All.Where(t => t.Shortcut is not null).ToList();
        Assert.Equal(7, withShortcut.Count);
        Assert.Equal(7, withShortcut.Select(t => char.ToUpperInvariant(t.Shortcut!.Value)).Distinct().Count());

        Assert.Equal(FreeformTool.Select, FreeformToolCatalog.FromShortcut('V'));
        Assert.Equal(FreeformTool.Pan, FreeformToolCatalog.FromShortcut('h'));
        Assert.Equal(FreeformTool.Text, FreeformToolCatalog.FromShortcut('T'));
        Assert.Equal(FreeformTool.Rect, FreeformToolCatalog.FromShortcut('R'));
        Assert.Equal(FreeformTool.Arrow, FreeformToolCatalog.FromShortcut('A'));
        Assert.Equal(FreeformTool.Ink, FreeformToolCatalog.FromShortcut('B'));
        Assert.Equal(FreeformTool.Eraser, FreeformToolCatalog.FromShortcut('E'));

        // 没分到快捷键的键不能误命中
        Assert.Null(FreeformToolCatalog.FromShortcut('Q'));
        Assert.Null(FreeformToolCatalog.FromShortcut('1'));
        Assert.Null(FreeformToolCatalog.FromShortcut('中'));
    }

    [Theory]
    [InlineData("select", FreeformTool.Select)]
    [InlineData("pan", FreeformTool.Pan)]
    [InlineData("ink", FreeformTool.Ink)]
    [InlineData("INK", FreeformTool.Ink)]
    [InlineData(" ink ", FreeformTool.Ink)]
    [InlineData("zoom", FreeformTool.Zoom)]
    public void Parse_KnownIds(string id, FreeformTool expected)
    {
        Assert.Equal(expected, FreeformToolCatalog.Parse(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("skynet")]
    public void Parse_UnknownFallsBackWithoutThrowing(string? id)
    {
        Assert.Equal(FreeformTool.Select, FreeformToolCatalog.Parse(id));
        Assert.Equal(FreeformTool.Eraser, FreeformToolCatalog.Parse(id, FreeformTool.Eraser));
    }

    [Fact]
    public void IdOf_RoundTripsEveryTool()
    {
        foreach (var tool in Enum.GetValues<FreeformTool>())
        {
            var id = FreeformToolCatalog.IdOf(tool);
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.Equal(tool, FreeformToolCatalog.Parse(id));
        }
    }

    [Fact]
    public void Zoom_ClampsBothEnds_To25And400Percent()
    {
        var zoom = 1.0;
        for (var i = 0; i < 40; i++)
        {
            zoom = FreeformZoom.Next(zoom, zoomIn: true);
        }

        Assert.Equal(CanvasViewport.MaxZoom, zoom);
        Assert.Equal(400d, FreeformZoom.Percent(zoom));

        for (var i = 0; i < 40; i++)
        {
            zoom = FreeformZoom.Next(zoom, zoomIn: false);
        }

        Assert.Equal(CanvasViewport.MinZoom, zoom);
        Assert.Equal(25d, FreeformZoom.Percent(zoom));

        // 从 100% 出发，两个方向都必须在有限步内抵达端点（不是「理论上可达」）
        var up = 1.0;
        var upSteps = 0;
        while (up < CanvasViewport.MaxZoom && upSteps < 50)
        {
            up = FreeformZoom.Next(up, zoomIn: true);
            upSteps++;
        }

        Assert.Equal(CanvasViewport.MaxZoom, up);
        Assert.True(upSteps <= 10);

        var down = 1.0;
        var downSteps = 0;
        while (down > CanvasViewport.MinZoom && downSteps < 50)
        {
            down = FreeformZoom.Next(down, zoomIn: false);
            downSteps++;
        }

        Assert.Equal(CanvasViewport.MinZoom, down);
        Assert.True(downSteps <= 20);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Zoom_BadInputFallsBackTo100Percent(double bad)
    {
        Assert.Equal(1.0, FreeformZoom.Clamp(bad));
        Assert.Equal(1.0, FreeformZoom.Clamp(double.NaN));
        Assert.Equal(1.0, FreeformZoom.Clamp(double.PositiveInfinity));
        Assert.Equal(1.0, FreeformZoom.Clamp(0));
        Assert.Equal(1.0, FreeformZoom.Clamp(-3));
    }

    [Fact]
    public void Fit_LeavesPaddingAndFallsBackWhenEmpty()
    {
        var fit = FreeformZoom.Fit(400, 300, 800, 600);
        Assert.Equal(Math.Min((800 - 48) / 400d, (600 - 48) / 300d), fit, 6);

        // 内容极端大时不允许越过 25% 下限（宁可看不全，也不返回 0）
        Assert.Equal(CanvasViewport.MinZoom, FreeformZoom.Fit(40000, 20000, 800, 600));

        Assert.Equal(1.0, FreeformZoom.Fit(0, 300, 800, 600));
        Assert.Equal(1.0, FreeformZoom.Fit(400, 0, 800, 600));
        Assert.Equal(1.0, FreeformZoom.Fit(400, 300, 0, 600));
    }

    [Fact]
    public void CanvasViewport_Normalize_ClampsZoomAndKeepsOffsets()
    {
        var viewport = new CanvasViewport { Zoom = 9.5, OffsetX = 12, OffsetY = -8 };
        viewport.Normalize();
        Assert.Equal(CanvasViewport.MaxZoom, viewport.Zoom);
        Assert.Equal(12, viewport.OffsetX);
        Assert.Equal(-8, viewport.OffsetY);

        var tiny = new CanvasViewport { Zoom = 0.01 };
        tiny.Normalize();
        Assert.Equal(CanvasViewport.MinZoom, tiny.Zoom);
    }
}
