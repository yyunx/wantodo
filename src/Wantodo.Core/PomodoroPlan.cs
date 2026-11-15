namespace Wantodo.Core;

/// <summary>番茄钟的一段：连续做 <see cref="Repeat"/> 组，每组专注 + 休息。</summary>
public sealed class PomodoroSegment
{
    public int Repeat { get; set; } = 1;

    public int FocusSeconds { get; set; } = 25 * 60;

    public int BreakSeconds { get; set; } = 5 * 60;
}

/// <summary>
/// 番茄钟计划：简洁版就是一段（重复 N 组），自定义版可以前几组 50/10、后几组 25/5。
/// 时间轴是纯函数，UI 只消费它，不自己算下一组。
/// </summary>
public sealed class PomodoroPlan
{
    public int TotalGroups { get; set; } = 4;

    public List<PomodoroSegment> Segments { get; set; } = new();

    public static PomodoroPlan CreateDefault() => new()
    {
        TotalGroups = 4,
        Segments = { new PomodoroSegment { Repeat = 4, FocusSeconds = 25 * 60, BreakSeconds = 5 * 60 } },
    };

    public void Normalize()
    {
        TotalGroups = Math.Clamp(TotalGroups, 1, 64);
        Segments.RemoveAll(s => s is null || s.Repeat <= 0);
        foreach (var segment in Segments)
        {
            segment.FocusSeconds = Math.Clamp(segment.FocusSeconds, 1, 24 * 3600);
            segment.BreakSeconds = Math.Clamp(segment.BreakSeconds, 0, 24 * 3600);
        }

        if (Segments.Count == 0)
        {
            Segments.Add(new PomodoroSegment { Repeat = TotalGroups, FocusSeconds = 25 * 60, BreakSeconds = 5 * 60 });
        }
    }

    /// <summary>
    /// 展开成「第几组、专注还是休息、多少秒」的序列。最后一组的休息不排，
    /// 因为番茄钟的结尾是「做完最后一组」，不是「再休息一轮」。
    /// </summary>
    public IReadOnlyList<(int Group, bool IsBreak, int Seconds)> Timeline()
    {
        Normalize();

        var timeline = new List<(int Group, bool IsBreak, int Seconds)>();
        for (int group = 1; group <= TotalGroups; group++)
        {
            PomodoroSegment segment = SegmentFor(group);
            timeline.Add((group, false, segment.FocusSeconds));
            if (group < TotalGroups)
            {
                timeline.Add((group, true, segment.BreakSeconds));
            }
        }

        return timeline;
    }

    private PomodoroSegment SegmentFor(int group)
    {
        int cursor = 0;
        foreach (var segment in Segments)
        {
            cursor += segment.Repeat;
            if (group <= cursor)
            {
                return segment;
            }
        }

        return Segments[^1];
    }
}
