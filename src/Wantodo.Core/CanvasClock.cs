using System;
using System.Globalization;

namespace Wantodo.Core;

/// <summary>计时卡片的四种玩法。</summary>
public enum TimerMode
{
    /// <summary>正计时：从 0 往上数，不停就不结束。</summary>
    Stopwatch,

    /// <summary>倒计时：到 0 结束一次。</summary>
    Countdown,

    /// <summary>闹钟：本质也是倒计时，只是含义上按「多久之后提醒」用。</summary>
    Alarm,

    /// <summary>番茄钟：25 分钟倒计时，到点提醒一次。</summary>
    Pomodoro,
}

/// <summary>
/// 计时卡片的纯逻辑：只存「开始时刻 + 总秒数」，显示时按当前时间算，
/// 不依赖 DispatcherTimer，也不把每秒的剩余值写进 canvas.json。
/// </summary>
public static class CanvasClock
{
    public const int DefaultCountdownSeconds = 25 * 60;

    public const int DefaultPomodoroSeconds = 25 * 60;

    public static TimerMode ParseMode(string? raw) => raw switch
    {
        "stopwatch" => TimerMode.Stopwatch,
        "alarm" => TimerMode.Alarm,
        "pomodoro" => TimerMode.Pomodoro,
        _ => TimerMode.Countdown,
    };

    public static string ModeKey(TimerMode mode) => mode switch
    {
        TimerMode.Stopwatch => "stopwatch",
        TimerMode.Alarm => "alarm",
        TimerMode.Pomodoro => "pomodoro",
        _ => "countdown",
    };

    public static string ModeLabel(TimerMode mode) => mode switch
    {
        TimerMode.Stopwatch => "正计时",
        TimerMode.Alarm => "闹钟",
        TimerMode.Pomodoro => "番茄钟",
        _ => "倒计时",
    };

    public static int DefaultSeconds(TimerMode mode) => mode switch
    {
        TimerMode.Stopwatch => 0,
        TimerMode.Pomodoro => DefaultPomodoroSeconds,
        _ => DefaultCountdownSeconds,
    };

    public static int ElapsedSeconds(string? startedAtIso, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(startedAtIso)
            || !DateTimeOffset.TryParse(startedAtIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
        {
            return 0;
        }

        double seconds = (now - started).TotalSeconds;
        return seconds <= 0 ? 0 : (int)Math.Floor(seconds);
    }

    /// <summary>卡片上要显示的数字：正计时往上走，其余往下走且不小于 0。</summary>
    public static int DisplaySeconds(TimerMode mode, int durationSeconds, string? startedAtIso, DateTimeOffset now)
    {
        int elapsed = ElapsedSeconds(startedAtIso, now);
        if (mode == TimerMode.Stopwatch)
        {
            return elapsed;
        }

        return Math.Max(0, durationSeconds - elapsed);
    }

    public static bool IsFinished(TimerMode mode, int durationSeconds, string? startedAtIso, DateTimeOffset now)
    {
        if (mode == TimerMode.Stopwatch || string.IsNullOrWhiteSpace(startedAtIso))
        {
            return false;
        }

        return ElapsedSeconds(startedAtIso, now) >= durationSeconds;
    }

    /// <summary>25:00 / 1:05:09 这种。超过一小时才显示小时位。</summary>
    public static string Format(int seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        int hours = seconds / 3600;
        int minutes = seconds % 3600 / 60;
        int secs = seconds % 60;
        return hours > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, secs)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes, secs);
    }
}
