using System;
using System.Linq;
using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>
/// P1.4 分段番茄钟：时间轴是纯函数，同一输入必须得到同一剩余秒（plan §5.5）。
/// </summary>
public sealed class PomodoroTimelineTests
{
    [Fact]
    public void DefaultPlan_IsFourGroupsOf25And5_WithoutTrailingBreak()
    {
        var plan = PomodoroPlan.CreateDefault();

        Assert.Equal(4, plan.TotalGroups);
        Assert.Single(plan.Segments);
        Assert.Equal(4 * 25 * 60 + 3 * 5 * 60, plan.TotalSeconds());
        Assert.Equal(7, plan.Timeline().Count);
        Assert.All(plan.Timeline().Take(6).Where((_, i) => i % 2 == 1), step => Assert.True(step.IsBreak));
    }

    [Fact]
    public void At_ReturnsFocusThenBreakPerGroup()
    {
        var plan = PomodoroPlan.CreateDefault();

        Assert.Equal(new PomodoroStep(1, false, 1500, 1500, false), plan.At(0));
        Assert.Equal(new PomodoroStep(1, false, 1500, 1, false), plan.At(1499));
        Assert.Equal(new PomodoroStep(1, true, 300, 300, false), plan.At(1500));
        Assert.Equal(new PomodoroStep(2, false, 1500, 1200, false), plan.At(1500 + 300 + 300));
    }

    [Fact]
    public void At_PastTheEnd_StopsOnLastGroupWithZeroRemaining()
    {
        var plan = PomodoroPlan.CreateDefault();
        var step = plan.At(plan.TotalSeconds() + 9999);

        Assert.True(step.Finished);
        Assert.Equal(0, step.RemainingSeconds);
        Assert.Equal(4, step.Group);
    }

    [Fact]
    public void At_IsDeterministic_ForSameInput()
    {
        var plan = PomodoroPlan.CreateDefault();
        var first = plan.At(1234);
        var second = plan.At(1234);
        Assert.Equal(first, second);
    }

    [Fact]
    public void SegmentedPlan_CrossesSegmentBoundaryCorrectly()
    {
        // 前 2 组 50/10，后 2 组 25/5
        var plan = new PomodoroPlan
        {
            TotalGroups = 4,
            Segments =
            {
                new PomodoroSegment { Repeat = 2, FocusSeconds = 50 * 60, BreakSeconds = 10 * 60 },
                new PomodoroSegment { Repeat = 2, FocusSeconds = 25 * 60, BreakSeconds = 5 * 60 },
            },
        };

        // 第 1 组 50 分钟专注 → 第 1 组 10 分钟休息 → 第 2 组还是 50 分钟
        Assert.Equal(50 * 60, plan.At(0).SegmentSeconds);
        Assert.Equal(10 * 60, plan.At(50 * 60).SegmentSeconds);
        Assert.Equal(50 * 60, plan.At(60 * 60).SegmentSeconds);
        // 第 3 组开始变 25 分钟
        Assert.Equal(25 * 60, plan.At(2 * 60 * 60).SegmentSeconds);
        // 专注 2×(50+25) + 组间休息 2×10+1×5（最后一组不加休息）
        Assert.Equal(2 * 50 * 60 + 2 * 25 * 60 + 10 * 60 + 10 * 60 + 5 * 60, plan.TotalSeconds());
    }

    [Fact]
    public void SingleSegmentPlan_IsTheSimpleVersion()
    {
        var plan = new PomodoroPlan
        {
            TotalGroups = 2,
            Segments = { new PomodoroSegment { Repeat = 2, FocusSeconds = 30 * 60, BreakSeconds = 5 * 60 } },
        };

        Assert.Equal(2 * 30 * 60 + 5 * 60, plan.TotalSeconds());
        Assert.Equal(30 * 60, plan.At(0).SegmentSeconds);
        Assert.Equal(5 * 60, plan.At(30 * 60).SegmentSeconds);
    }

    [Fact]
    public void Normalize_RepairsExtremeValues()
    {
        var plan = new PomodoroPlan
        {
            TotalGroups = 0,
            Segments = { new PomodoroSegment { Repeat = 0, FocusSeconds = -5, BreakSeconds = 999999 } },
        };
        plan.Normalize();

        Assert.Equal(1, plan.TotalGroups);
        Assert.Single(plan.Segments);
        Assert.True(plan.Segments[0].Repeat > 0);
        Assert.InRange(plan.Segments[0].FocusSeconds, 1, 24 * 3600);
        Assert.InRange(plan.Segments[0].BreakSeconds, 0, 24 * 3600);
    }

    [Fact]
    public void ToKey_RoundTripsSegmentedPlan()
    {
        var plan = new PomodoroPlan
        {
            TotalGroups = 4,
            Segments =
            {
                new PomodoroSegment { Repeat = 2, FocusSeconds = 50 * 60, BreakSeconds = 10 * 60 },
                new PomodoroSegment { Repeat = 2, FocusSeconds = 25 * 60, BreakSeconds = 5 * 60 },
            },
        };

        var copy = PomodoroPlan.Parse(plan.ToKey());

        Assert.Equal(plan.TotalGroups, copy.TotalGroups);
        Assert.Equal(plan.Segments.Count, copy.Segments.Count);
        Assert.Equal(plan.TotalSeconds(), copy.TotalSeconds());
        Assert.Equal(plan.Timeline(), copy.Timeline());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("4|only-one-part")]
    [InlineData("4|2:3000:600;bad")]
    [InlineData("0|2:3000:600")]
    public void Parse_BadKeysFallBackToDefault(string? raw)
    {
        var plan = PomodoroPlan.Parse(raw);

        Assert.Equal(4, plan.TotalGroups);
        Assert.Single(plan.Segments);
        Assert.Equal(25 * 60, plan.Segments[0].FocusSeconds);
    }

    [Fact]
    public void Parse_ClampsGroupsAndKeepsSegmentsCountBounded()
    {
        var plan = PomodoroPlan.Parse("999|2:3000:600");

        Assert.InRange(plan.TotalGroups, 1, 64);
        Assert.Single(plan.Segments);
    }
}
