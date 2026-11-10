using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wantodo.App.Interop;
using Wantodo.App.ViewModels;
using Wantodo.Core;

namespace Wantodo.App;

/// <summary>
/// 设置面板。所有改动立即生效并落盘（没有"确定/取消"——这是桌面小组件，不是文档编辑器）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private readonly BoardViewModel _viewModel;
    private bool _loading;

    public SettingsWindow(MainWindow main, BoardViewModel viewModel)
    {
        _main = main;
        _viewModel = viewModel;

        InitializeComponent();
        LoadCurrentValues();
    }

    private void LoadCurrentValues()
    {
        _loading = true;
        try
        {
            var surface = _viewModel.Model.Surface;
            DesktopRadio.IsChecked = string.Equals(surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase);
            NormalRadio.IsChecked = string.Equals(surface, DesktopLayer.SurfaceNormal, StringComparison.OrdinalIgnoreCase);
            TopmostRadio.IsChecked = string.Equals(surface, DesktopLayer.SurfaceTopmost, StringComparison.OrdinalIgnoreCase);
            if (DesktopRadio.IsChecked != true && NormalRadio.IsChecked != true && TopmostRadio.IsChecked != true)
            {
                DesktopRadio.IsChecked = true;
            }

            DarkRadio.IsChecked = !ThemePalette.IsLight(_viewModel.Model.Theme);
            LightRadio.IsChecked = ThemePalette.IsLight(_viewModel.Model.Theme);

            ToneSlider.Value = Math.Clamp(_viewModel.Model.Tone, 0, 1);

            bool autoFit = !string.Equals(
                _viewModel.Model.Placement.SizeMode, SizeModes.Manual, StringComparison.OrdinalIgnoreCase);
            AutoFitRadio.IsChecked = autoFit;
            ManualSizeRadio.IsChecked = !autoFit;

            HideCompletedCheck.IsChecked = _viewModel.HideCompleted;
            AutostartCheck.IsChecked = Shell.Autostart.IsEnabled();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnSurfaceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { IsChecked: true } radio)
        {
            return;
        }

        var surface =
            ReferenceEquals(radio, NormalRadio) ? DesktopLayer.SurfaceNormal :
            ReferenceEquals(radio, TopmostRadio) ? DesktopLayer.SurfaceTopmost :
            DesktopLayer.SurfaceDesktop;

        _main.SetSurface(surface);
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { IsChecked: true } radio)
        {
            return;
        }

        _viewModel.SetTheme(ReferenceEquals(radio, LightRadio) ? ThemePalette.LightTheme : ThemePalette.DarkTheme);
    }

    private void OnToneChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        _viewModel.SetTone(e.NewValue);
    }

    private void OnSizeModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { IsChecked: true } radio)
        {
            return;
        }

        _main.SetSizeMode(
            ReferenceEquals(radio, ManualSizeRadio) ? SizeModes.Manual : SizeModes.AutoFit,
            "设置面板");
    }

    private void OnHideCompletedChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _viewModel.HideCompleted = HideCompletedCheck.IsChecked == true;
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var wanted = AutostartCheck.IsChecked == true;
        var actual = Shell.Autostart.SetEnabled(wanted);
        if (actual != wanted)
        {
            _loading = true;
            AutostartCheck.IsChecked = actual;
            _loading = false;
            MessageBox.Show(this, "写入注册表失败，自启设置没有生效（详见 app.log）。", "wantodo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 鼠标在按下瞬间已经不是按下状态，忽略
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _main.OnSettingsClosed();
        base.OnClosed(e);
    }
}
