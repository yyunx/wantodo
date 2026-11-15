namespace Wantodo.Core;

/// <summary>
/// 计时卡的状态机：<c>Idle → Configured → Running ⇄ Paused → Done</c>。
/// 只负责「状态 → 按钮文案 / 默认态」的映射，不持有时间。
/// </summary>
public enum TimerState
{
    /// <summary>刚建出来，还没设置过时长。</summary>
    Idle = 0,

    /// <summary>设置完成，等着人按开始（配置完不允许自动跑）。</summary>
    Configured = 1,

    Running = 2,

    Paused = 3,

    /// <summary>跑到点，提醒已发。</summary>
    Done = 4,
}

/// <summary>状态机的纯逻辑：解析/序列化状态键，给出卡片按钮文案与下一状态。</summary>
public static class TimerStates
{
    public const string Idle = "idle";

    public const string Configured = "configured";

    public const string Running = "running";

    public const string Paused = "paused";

    public const string Done = "done";

    public static string Key(TimerState state) => state switch
    {
        TimerState.Configured => Configured,
        TimerState.Running => Running,
        TimerState.Paused => Paused,
        TimerState.Done => Done,
        _ => Idle,
    };

    public static TimerState Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        Configured => TimerState.Configured,
        Running => TimerState.Running,
        Paused => TimerState.Paused,
        Done => TimerState.Done,
        _ => TimerState.Idle,
    };

    /// <summary>
    /// 旧卡片只有 startedAt / elapsed / fired 三个字段，没有 state。
    /// 这里按「有开始时刻就是在跑、攒过时间就是暂停、提醒过就是完成」反推，
    /// 保证 0.6.0 的老卡片打开后按钮文案不会错。
    /// </summary>
    public static TimerState Infer(string? key, string? startedAt, int elapsedSeconds, string? fired)
    {
        TimerState state = Parse(key);
        if (state != TimerState.Idle)
        {
            return state;
        }

        if (!string.IsNullOrWhiteSpace(startedAt))
        {
            return TimerState.Running;
        }

        if (string.Equals(fired, "1", StringComparison.Ordinal))
        {
            return TimerState.Done;
        }

        return elapsedSeconds > 0 ? TimerState.Paused : TimerState.Idle;
    }

    /// <summary>卡片上那个主按钮的文案。Done 之后按「重新开始」，重新设置改成「设置」按钮的事。</summary>
    public static string ActionLabel(TimerState state) => state switch
    {
        TimerState.Running => "暂停",
        TimerState.Paused => "继续",
        TimerState.Done => "重新开始",
        _ => "开始",
    };

    /// <summary>按下主按钮后该进入的状态。</summary>
    public static TimerState AfterPrimaryAction(TimerState state) => state switch
    {
        TimerState.Running => TimerState.Paused,
        TimerState.Idle or TimerState.Configured or TimerState.Paused or TimerState.Done => TimerState.Running,
        _ => TimerState.Running,
    };

    /// <summary>设置完成时的落点：永远是 Configured，不自动开跑。</summary>
    public static TimerState AfterConfigure => TimerState.Configured;

    public static string Label(TimerState state) => state switch
    {
        TimerState.Configured => "已设置",
        TimerState.Running => "进行中",
        TimerState.Paused => "已暂停",
        TimerState.Done => "已结束",
        _ => "未开始",
    };
}
