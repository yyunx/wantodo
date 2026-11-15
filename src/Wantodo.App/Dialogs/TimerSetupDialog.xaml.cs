using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wantodo.Core;

namespace Wantodo.App.Dialogs;

/// <summary>选计时玩法和时长。正计时不需要时长，其余至少 1 秒；番茄钟还能分段。</summary>
public partial class TimerSetupDialog : Window
{
    private const int MaxSegments = 4;

    private readonly List<PomodoroSegment> _segments = new();

    private bool _syncing;

    public TimerSetupDialog(TimerMode mode, int seconds)
        : this(mode, seconds, null)
    {
    }

    public TimerSetupDialog(TimerMode mode, int seconds, PomodoroPlan? plan)
    {
        InitializeComponent();
        Seconds = seconds;
        Plan = plan ?? PomodoroPlan.CreateDefault();
        foreach (var segment in Plan.Segments)
        {
            _segments.Add(new PomodoroSegment
            {
                Repeat = segment.Repeat,
                FocusSeconds = segment.FocusSeconds,
                BreakSeconds = segment.BreakSeconds,
            });
        }

        if (_segments.Count == 0)
        {
            _segments.Add(new PomodoroSegment { Repeat = 4, FocusSeconds = 25 * 60, BreakSeconds = 5 * 60 });
        }

        Select(mode);
        LoadPomodoro();
        UpdateDurationVisibility();
        Loaded += (_, _) => MinutesBox.Focus();
    }

    public TimerMode Mode { get; private set; } = TimerMode.Countdown;

    public int Seconds { get; private set; }

    public PomodoroPlan Plan { get; private set; }

    private void Select(TimerMode mode)
    {
        Mode = mode;
        _syncing = true;
        StopwatchRadio.IsChecked = mode == TimerMode.Stopwatch;
        CountdownRadio.IsChecked = mode == TimerMode.Countdown;
        AlarmRadio.IsChecked = mode == TimerMode.Alarm;
        PomodoroRadio.IsChecked = mode == TimerMode.Pomodoro;
        _syncing = false;

        int total = Math.Max(0, Seconds);
        MinutesBox.Text = (total / 60).ToString(CultureInfo.InvariantCulture);
        SecondsBox.Text = (total % 60).ToString(CultureInfo.InvariantCulture);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _syncing)
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
        DurationPanel.Visibility = Mode is TimerMode.Stopwatch ? Visibility.Collapsed : Visibility.Visible;
        PomodoroPanel.Visibility = Mode == TimerMode.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
        SimplePanel.Visibility = CustomCheck.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        CustomPanel.Visibility = CustomCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Hint.Text = Mode switch
        {
            TimerMode.Stopwatch => "正计时从 0 往上数，想停时按卡片上的 ■。",
            TimerMode.Alarm => "到点后右下角提醒一次，并响一声。",
            TimerMode.Pomodoro => "配置完不会自动开始；卡片上按「开始」才走。",
            _ => "到点后右下角提醒一次，并响一声。",
        };
    }

    private void LoadPomodoro()
    {
        var first = _segments[0];
        GroupsBox.Text = first.Repeat.ToString(CultureInfo.InvariantCulture);
        FocusBox.Text = (first.FocusSeconds / 60).ToString(CultureInfo.InvariantCulture);
        BreakBox.Text = (first.BreakSeconds / 60).ToString(CultureInfo.InvariantCulture);

        // 多段的自定义计划：勾上「自定义」并逐段铺出来，简洁版这时只是只读的「已自定义」
        bool custom = _segments.Count > 1;
        CustomCheck.IsChecked = custom;
        PomodoroHint.Text = custom
            ? $"已自定义 {_segments.Count} 段；改简洁版会压成一段。"
            : "简洁版 = 一段重复 N 组；要不等长的段请勾自定义。";
        RenderSegments();
    }

    private void RenderSegments()
    {
        SegmentRows.Children.Clear();
        for (int i = 0; i < _segments.Count; i++)
        {
            int index = i;
            var segment = _segments[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new TextBlock
            {
                Text = $"第 {i + 1} 段",
                Width = 52,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecondary"),
                FontSize = 11,
            });
            row.Children.Add(NumberBox(segment.Repeat, "组", value => segment.Repeat = value));
            row.Children.Add(NumberBox(segment.FocusSeconds / 60, "专注(分)", value => segment.FocusSeconds = value * 60));
            row.Children.Add(NumberBox(segment.BreakSeconds / 60, "休息(分)", value => _segments[index].BreakSeconds = value * 60));
            SegmentRows.Children.Add(row);
        }
    }

    private FrameworkElement NumberBox(int value, string label, Action<int> apply)
    {
        var box = new TextBox
        {
            Width = 44,
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(6, 0, 6, 0),
            Text = value.ToString(CultureInfo.InvariantCulture),
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimary"),
            Background = (Brush)FindResource("CardBackground"),
            BorderBrush = (Brush)FindResource("Divider"),
        };
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim(), out int parsed) && parsed >= 0)
            {
                apply(parsed);
            }
        };

        var host = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        host.Children.Add(box);
        host.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondary"),
        });
        return host;
    }

    private void OnCustomChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        if (CustomCheck.IsChecked != true && _segments.Count > 1)
        {
            // 简洁版会把多段压成一段；有自定义就先把选择权交回用户
            var answer = MessageBox.Show(
                this,
                "改回简洁版会把自定义分段压成一段，继续吗？",
                "番茄钟",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                CustomCheck.IsChecked = true;
                return;
            }

            var first = _segments[0];
            _segments.Clear();
            _segments.Add(first);
            RenderSegments();
        }

        if (CustomCheck.IsChecked == true && _segments.Count == 0)
        {
            _segments.Add(new PomodoroSegment { Repeat = 1, FocusSeconds = 25 * 60, BreakSeconds = 5 * 60 });
            RenderSegments();
        }

        PomodoroHint.Text = CustomCheck.IsChecked == true
            ? $"自定义 {_segments.Count} 段；最多 {MaxSegments} 段。"
            : "简洁版 = 一段重复 N 组；要不等长的段请勾自定义。";
        UpdateDurationVisibility();
    }

    private void OnAddSegment(object sender, RoutedEventArgs e)
    {
        if (_segments.Count >= MaxSegments)
        {
            PomodoroHint.Text = $"最多 {MaxSegments} 段。";
            return;
        }

        var last = _segments[^1];
        _segments.Add(new PomodoroSegment
        {
            Repeat = 1,
            FocusSeconds = last.FocusSeconds,
            BreakSeconds = last.BreakSeconds,
        });
        RenderSegments();
        PomodoroHint.Text = $"自定义 {_segments.Count} 段；最多 {MaxSegments} 段。";
    }

    private void OnRemoveSegment(object sender, RoutedEventArgs e)
    {
        if (_segments.Count <= 1)
        {
            PomodoroHint.Text = "至少留一段。";
            return;
        }

        _segments.RemoveAt(_segments.Count - 1);
        RenderSegments();
        PomodoroHint.Text = $"自定义 {_segments.Count} 段；最多 {MaxSegments} 段。";
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

        if (Mode == TimerMode.Pomodoro)
        {
            if (!TryBuildPlan(out var plan))
            {
                return;
            }

            Plan = plan;
            Seconds = plan.TotalSeconds();
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

    private bool TryBuildPlan(out PomodoroPlan plan)
    {
        plan = PomodoroPlan.CreateDefault();
        if (CustomCheck.IsChecked == true)
        {
            if (_segments.Count == 0 || _segments.Count > MaxSegments)
            {
                PomodoroHint.Text = $"分段数要在 1–{MaxSegments} 之间。";
                return false;
            }

            if (_segments.Exists(s => s.FocusSeconds <= 0))
            {
                PomodoroHint.Text = "每段专注至少 1 分钟。";
                return false;
            }

            plan = new PomodoroPlan
            {
                TotalGroups = Math.Clamp(_segments.Sum(s => Math.Max(1, s.Repeat)), 1, 64),
                Segments = _segments.Select(s => new PomodoroSegment
                {
                    Repeat = Math.Max(1, s.Repeat),
                    FocusSeconds = s.FocusSeconds,
                    BreakSeconds = Math.Max(0, s.BreakSeconds),
                }).ToList(),
            };
            plan.Normalize();
            return true;
        }

        int groups = Parse(GroupsBox.Text, 4);
        int focus = Parse(FocusBox.Text, 25);
        int rest = Parse(BreakBox.Text, 5);
        if (focus <= 0)
        {
            PomodoroHint.Text = "专注至少 1 分钟。";
            return false;
        }

        groups = Math.Clamp(groups, 1, 64);

        // 简洁版始终只有一段：写回时压成单段（plan §5.5）
        plan = new PomodoroPlan
        {
            TotalGroups = groups,
            Segments = new List<PomodoroSegment>
            {
                new() { Repeat = groups, FocusSeconds = focus * 60, BreakSeconds = Math.Max(0, rest) * 60 },
            },
        };
        plan.Normalize();
        return true;
    }

    private static int Parse(string text, int fallback)
        => int.TryParse(text.Trim(), out int value) && value >= 0 ? value : fallback;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
