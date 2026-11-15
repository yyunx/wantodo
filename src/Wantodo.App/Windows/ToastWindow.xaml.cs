using System;
using System.Windows;
using System.Windows.Threading;

namespace Wantodo.App.Windows;

/// <summary>
/// 右下角一次性提醒。不抢焦点、不写任务栏，4 秒后自己关；
/// 同一时刻只留一条，新的提醒顶掉旧的。
/// </summary>
public partial class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _timer;

    public ToastWindow(string title, string body)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += (_, _) => Close();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        };
    }

    public static void Show(string title, string body)
    {
        _current?.Close();
        var toast = new ToastWindow(title, body);
        _current = toast;
        toast.Show();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
        _timer.Start();
    }
}
