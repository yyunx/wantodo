using System;
using System.Globalization;
using System.Windows;
using Wantodo.Core;

namespace Wantodo.App.Dialogs;

/// <summary>选计时玩法和时长。正计时不需要时长，其余至少 1 秒。</summary>
public partial class TimerSetupDialog : Window
{
    public TimerSetupDialog(TimerMode mode, int seconds)
    {
        InitializeComponent();
        Seconds = seconds;
        Select(mode);
        UpdateDurationVisibility();
        Loaded += (_, _) => MinutesBox.Focus();
    }

    public TimerMode Mode { get; private set; } = TimerMode.Countdown;

    public int Seconds { get; private set; }

    private void Select(TimerMode mode)
    {
        Mode = mode;
        StopwatchRadio.IsChecked = mode == TimerMode.Stopwatch;
        CountdownRadio.IsChecked = mode == TimerMode.Countdown;
        AlarmRadio.IsChecked = mode == TimerMode.Alarm;
        PomodoroRadio.IsChecked = mode == TimerMode.Pomodoro;

        int total = Math.Max(0, Seconds);
        MinutesBox.Text = (total / 60).ToString(CultureInfo.InvariantCulture);
        SecondsBox.Text = (total % 60).ToString(CultureInfo.InvariantCulture);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (PomodoroRadio.IsChecked == true)
        {
            MinutesBox.Text = "25";
            SecondsBox.Text = "0";
        }

        UpdateDurationVisibility();
    }

    private void UpdateDurationVisibility()
    {
        if (DurationPanel is null)
        {
            return;
        }

        Mode = StopwatchRadio.IsChecked == true ? TimerMode.Stopwatch
            : AlarmRadio.IsChecked == true ? TimerMode.Alarm
            : PomodoroRadio.IsChecked == true ? TimerMode.Pomodoro
            : TimerMode.Countdown;
        DurationPanel.Visibility = Mode == TimerMode.Stopwatch ? Visibility.Collapsed : Visibility.Visible;
        Hint.Text = Mode switch
        {
            TimerMode.Stopwatch => "正计时从 0 往上数，想停时按卡片上的 ■。",
            TimerMode.Alarm => "到点后右下角提醒一次，并响一声。",
            TimerMode.Pomodoro => "番茄钟固定 25 分钟，到点提醒一次。",
            _ => "到点后右下角提醒一次，并响一声。",
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        UpdateDurationVisibility();
        if (Mode == TimerMode.Stopwatch)
        {
            Seconds = 0;
            DialogResult = true;
            return;
        }

        if (!int.TryParse(MinutesBox.Text.Trim(), out int minutes) || minutes < 0)
        {
            minutes = 0;
        }

        if (!int.TryParse(SecondsBox.Text.Trim(), out int seconds) || seconds < 0 || seconds > 59)
        {
            seconds = 0;
        }

        int total = minutes * 60 + seconds;
        if (total <= 0)
        {
            Hint.Text = "时长至少 1 秒。";
            return;
        }

        Seconds = total;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
