using System;
using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

public sealed class CanvasClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("stopwatch", TimerMode.Stopwatch)]
    [InlineData("alarm", TimerMode.Alarm)]
    [InlineData("pomodoro", TimerMode.Pomodoro)]
    [InlineData("countdown", TimerMode.Countdown)]
    [InlineData("nonsense", TimerMode.Countdown)]
    public void ParseMode_MapsKnownKeysAndFallsBackToCountdown(string raw, TimerMode expected)
    {
        Assert.Equal(expected, CanvasClock.ParseMode(raw));
    }

    [Fact]
    public void Stopwatch_CountsUpAndNeverFinishes()
    {
        var started = (Now - TimeSpan.FromSeconds(75)).ToString("O");

        Assert.Equal(75, CanvasClock.DisplaySeconds(TimerMode.Stopwatch, 0, started, Now));
        Assert.False(CanvasClock.IsFinished(TimerMode.Stopwatch, 0, started, Now));
    }

    [Fact]
    public void Countdown_CountsDownAndFinishesAtZero()
    {
        var started = (Now - TimeSpan.FromSeconds(60)).ToString("O");

        Assert.Equal(0, CanvasClock.DisplaySeconds(TimerMode.Countdown, 60, started, Now));
        Assert.True(CanvasClock.IsFinished(TimerMode.Countdown, 60, started, Now));
        Assert.Equal(30, CanvasClock.DisplaySeconds(TimerMode.Countdown, 90, started, Now));
    }

    [Fact]
    public void NotStarted_ShowsFullDurationAndIsNotFinished()
    {
        Assert.Equal(1500, CanvasClock.DisplaySeconds(TimerMode.Pomodoro, 1500, null, Now));
        Assert.False(CanvasClock.IsFinished(TimerMode.Pomodoro, 1500, null, Now));
        Assert.Equal(1500, CanvasClock.DisplaySeconds(TimerMode.Countdown, 1500, "bad iso", Now));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(59, "00:59")]
    [InlineData(1500, "25:00")]
    [InlineData(3725, "1:02:05")]
    public void Format_UsesMinutesOrHours(int seconds, string expected)
    {
        Assert.Equal(expected, CanvasClock.Format(seconds));
    }

    [Fact]
    public void DefaultSeconds_MatchTheMode()
    {
        Assert.Equal(0, CanvasClock.DefaultSeconds(TimerMode.Stopwatch));
        Assert.Equal(25 * 60, CanvasClock.DefaultSeconds(TimerMode.Pomodoro));
        Assert.Equal(25 * 60, CanvasClock.DefaultSeconds(TimerMode.Countdown));
    }

    [Fact]
    public void ElapsedSeconds_ClampsFutureStartToZero()
    {
        var future = (Now + TimeSpan.FromMinutes(5)).ToString("O");

        Assert.Equal(0, CanvasClock.ElapsedSeconds(future, Now));
    }

    [Fact]
    public void DisplaySeconds_UsesPausedBase()
    {
        Assert.Equal(75, CanvasClock.DisplaySeconds(TimerMode.Stopwatch, 0, null, 75, Now));
        Assert.Equal(1425, CanvasClock.DisplaySeconds(TimerMode.Countdown, 1500, null, 75, Now));
    }

    [Fact]
    public void AccumulatedSeconds_AddsBaseAndClampsToDuration()
    {
        var started = Now;

        Assert.Equal(90, CanvasClock.AccumulatedSeconds(TimerMode.Stopwatch, 0, 30, started, Now.AddMinutes(1)));
        Assert.Equal(1500, CanvasClock.AccumulatedSeconds(TimerMode.Countdown, 1500, 1400, started, Now.AddMinutes(5)));
    }

    [Fact]
    public void IsFinished_RespectsPausedBase()
    {
        var started = Now;

        Assert.False(CanvasClock.IsFinished(TimerMode.Countdown, 1500, started.ToString("O"), 0, Now.AddSeconds(10)));
        Assert.True(CanvasClock.IsFinished(TimerMode.Countdown, 1500, started.ToString("O"), 1495, Now.AddSeconds(10)));
    }
}
