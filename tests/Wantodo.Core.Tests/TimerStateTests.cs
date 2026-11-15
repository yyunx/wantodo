using System;
using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

/// <summary>
/// P1.4 计时状态机：Idle → Configured → Running ⇄ Paused → Done。
/// 这些断言对应 plan §5.5 的「全状态遍历按钮文案正确；配置完不自动跑；暂停恢复不丢剩余秒」。
/// </summary>
public sealed class TimerStateTests
{
    [Theory]
    [InlineData("idle", TimerState.Idle)]
    [InlineData("configured", TimerState.Configured)]
    [InlineData("running", TimerState.Running)]
    [InlineData("paused", TimerState.Paused)]
    [InlineData("done", TimerState.Done)]
    [InlineData("RUNNING", TimerState.Running)]
    [InlineData("  paused  ", TimerState.Paused)]
    [InlineData("", TimerState.Idle)]
    [InlineData(null, TimerState.Idle)]
    [InlineData("skynet", TimerState.Idle)]
    public void Parse_RecognizesEveryStateKey(string? key, TimerState expected)
    {
        Assert.Equal(expected, TimerStates.Parse(key));
    }

    [Fact]
    public void Key_RoundTripsEveryState()
    {
        foreach (TimerState state in Enum.GetValues<TimerState>())
        {
            Assert.Equal(state, TimerStates.Parse(TimerStates.Key(state)));
        }
    }

    [Theory]
    [InlineData(TimerState.Idle, "开始")]
    [InlineData(TimerState.Configured, "开始")]
    [InlineData(TimerState.Running, "暂停")]
    [InlineData(TimerState.Paused, "继续")]
    [InlineData(TimerState.Done, "重新开始")]
    public void ActionLabel_IsUniquePerState(TimerState state, string expected)
    {
        Assert.Equal(expected, TimerStates.ActionLabel(state));
    }

    [Fact]
    public void ActionLabels_SeparateStartPauseContinueRestart()
    {
        // Idle 与 Configured 都是「开始」是有意的：状态区别在状态行文案（未开始/已设置），不在按钮
        var actions = new[]
        {
            TimerStates.ActionLabel(TimerState.Running),
            TimerStates.ActionLabel(TimerState.Paused),
            TimerStates.ActionLabel(TimerState.Done),
            TimerStates.ActionLabel(TimerState.Idle),
        };

        Assert.Equal(actions.Length, actions.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(TimerStates.ActionLabel(TimerState.Idle), TimerStates.ActionLabel(TimerState.Configured));
    }

    [Fact]
    public void PrimaryAction_WalksTheMachine()
    {
        // Idle/Configured 按下就是开跑，Running 按下暂停，Paused 按下继续，Done 按下重跑
        Assert.Equal(TimerState.Running, TimerStates.AfterPrimaryAction(TimerState.Idle));
        Assert.Equal(TimerState.Running, TimerStates.AfterPrimaryAction(TimerState.Configured));
        Assert.Equal(TimerState.Paused, TimerStates.AfterPrimaryAction(TimerState.Running));
        Assert.Equal(TimerState.Running, TimerStates.AfterPrimaryAction(TimerState.Paused));
        Assert.Equal(TimerState.Running, TimerStates.AfterPrimaryAction(TimerState.Done));
    }

    [Fact]
    public void AfterConfigure_StaysConfiguredAndNeverStartsRunning()
    {
        Assert.Equal(TimerState.Configured, TimerStates.AfterConfigure);
    }

    [Fact]
    public void Infer_ReadsLegacyCardsWithoutStateField()
    {
        // 0.6.0 的卡片只有 startedAt / elapsed / fired
        Assert.Equal(TimerState.Running, TimerStates.Infer(null, "2026-11-16T00:00:00+08:00", 0, null));
        Assert.Equal(TimerState.Paused, TimerStates.Infer(null, "", 300, null));
        Assert.Equal(TimerState.Done, TimerStates.Infer(null, "", 1500, "1"));
        Assert.Equal(TimerState.Idle, TimerStates.Infer(null, "", 0, null));
    }

    [Fact]
    public void Infer_PrefersExplicitStateOverLegacyFields()
    {
        Assert.Equal(TimerState.Configured, TimerStates.Infer("configured", "2026-11-16T00:00:00+08:00", 0, null));
    }
}
