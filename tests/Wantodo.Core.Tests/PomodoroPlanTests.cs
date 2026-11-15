using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class PomodoroPlanTests
{
    [Fact]
    public void Default_IsTwentyFiveFiveTimesFour()
    {
        var timeline = PomodoroPlan.CreateDefault().Timeline();

        Assert.Equal(7, timeline.Count);
        Assert.Equal((1, false, 1500), timeline[0]);
        Assert.Equal((1, true, 300), timeline[1]);
        Assert.Equal((4, false, 1500), timeline[^1]);
        Assert.DoesNotContain(timeline, t => t.Group == 4 && t.IsBreak);
    }

    [Fact]
    public void CustomSegments_ExpandInOrder()
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

        var timeline = plan.Timeline();

        Assert.Equal(new[] { 3000, 600, 3000, 600, 1500, 300, 1500 }, timeline.Select(t => t.Seconds));
    }

    [Fact]
    public void ZeroRepeatSegments_AreDropped()
    {
        var plan = new PomodoroPlan
        {
            TotalGroups = 2,
            Segments =
            {
                new PomodoroSegment { Repeat = 0, FocusSeconds = 60, BreakSeconds = 10 },
            },
        };

        var timeline = plan.Timeline();

        Assert.Equal(3, timeline.Count);
        Assert.Equal(1500, timeline[0].Seconds);
    }

    [Fact]
    public void TotalGroups_IsClamped()
    {
        var plan = PomodoroPlan.CreateDefault();
        plan.TotalGroups = 0;

        Assert.Single(plan.Timeline());

        plan.TotalGroups = 500;
        plan.Normalize();
        Assert.Equal(64, plan.TotalGroups);
    }
}
