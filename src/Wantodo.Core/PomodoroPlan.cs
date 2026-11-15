using System.Linq;

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

    /// <summary>整份计划一共多长（秒）。末段休息不排，与 <see cref="Timeline"/> 一致。</summary>
    public int TotalSeconds()
    {
        int total = 0;
        foreach (var step in Timeline())
        {
            total += step.Seconds;
        }

        return total;
    }

    /// <summary>
    /// 时间轴求值：给定「已经走了多少秒」，返回当前是第几组、专注还是休息、本段多长、还剩多少。
    /// 纯函数，UI 只消费结果（plan §5.5：同一输入必须得到同一剩余秒）。
    /// </summary>
    public PomodoroStep At(int elapsedSeconds)
    {
        var timeline = Timeline();
        int elapsed = Math.Max(0, elapsedSeconds);
        int offset = 0;
        foreach (var step in timeline)
        {
            if (elapsed < offset + step.Seconds)
            {
                return new PomodoroStep(step.Group, step.IsBreak, step.Seconds, offset + step.Seconds - elapsed, false);
            }

            offset += step.Seconds;
        }

        // 已经超过整份计划：停在最后一组，剩余 0，标记做完了
        var last = timeline[^1];
        return new PomodoroStep(last.Group, last.IsBreak, last.Seconds, 0, true);
    }

    /// <summary>把计划压成一行字符串存进卡片 payload（不含逗号以外的结构，读回失败就回默认）。</summary>
    public string ToKey()
    {
        Normalize();
        var parts = Segments.Select(s => $"{s.Repeat}:{s.FocusSeconds}:{s.BreakSeconds}");
        return $"{TotalGroups}|{string.Join(";", parts)}";
    }

    /// <summary>从 <see cref="ToKey"/> 的格式读回；坏数据一律回默认计划，不抛。</summary>
    public static PomodoroPlan Parse(string? raw)
    {
        var plan = CreateDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return plan;
        }

        try
        {
            string[] halves = raw.Split('|');
            if (halves.Length != 2
                || !int.TryParse(halves[0], out int groups)
                || groups <= 0)
            {
                return plan;
            }

            var segments = new List<PomodoroSegment>();
            foreach (string chunk in halves[1].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] numbers = chunk.Split(':');
                if (numbers.Length != 3
                    || !int.TryParse(numbers[0], out int repeat)
                    || !int.TryParse(numbers[1], out int focus)
                    || !int.TryParse(numbers[2], out int rest)
                    || repeat <= 0)
                {
                    return plan;
                }

                segments.Add(new PomodoroSegment { Repeat = repeat, FocusSeconds = focus, BreakSeconds = rest });
            }

            if (segments.Count == 0)
            {
                return plan;
            }

            plan.TotalGroups = groups;
            plan.Segments = segments;
            plan.Normalize();
            return plan;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return plan;
        }
    }
}

/// <summary>时间轴上的一步：第几组、专注还是休息、本段总秒、剩余秒、整份计划是否已走完。</summary>
public readonly record struct PomodoroStep(int Group, bool IsBreak, int SegmentSeconds, int RemainingSeconds, bool Finished)
{
    public int ElapsedInSegment => Math.Max(0, SegmentSeconds - RemainingSeconds);

    public string Label => IsBreak ? $"第 {Group} 组休息" : $"第 {Group} 组专注";
}
