using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wantodo.App.Interop;
using Wantodo.App.ViewModels;
using Wantodo.Core;
using WinForms = System.Windows.Forms;

namespace Wantodo.App;

/// <summary>
/// 设置面板。所有改动立即生效并落盘（没有"确定/取消"——这是桌面小组件，不是文档编辑器）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private readonly BoardViewModel _viewModel;
    private readonly DataPaths _paths;
    private bool _loading;

    public SettingsWindow(MainWindow main, BoardViewModel viewModel, DataPaths paths)
    {
        _main = main;
        _viewModel = viewModel;
        _paths = paths;

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

            bool md = string.Equals(_viewModel.Model.DefaultNoteFormat, "md", StringComparison.OrdinalIgnoreCase);
            TxtRadio.IsChecked = !md;
            MdRadio.IsChecked = md;

            DataRootText.Text = _paths.Root;
            var free = Storage.DataRootService.FreeBytesAt(_paths.Root);
            DataFreeText.Text = $"所在磁盘可用 {DataMigrator.FormatBytes(free)}；大文件不会写进这个目录以外的地方。";
            CleanLegacyButton.IsEnabled = _main.LegacyDataExists();
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

    private void OnNoteFormatChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { IsChecked: true } radio)
        {
            return;
        }

        var format = ReferenceEquals(radio, MdRadio) ? "md" : "txt";
        _viewModel.SetDefaultNoteFormat(format);
        AppLog.Info($"随心记默认类型：{format}");
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

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        _main.OpenDataFolder();
    }

    private void OnMigrateClick(object sender, RoutedEventArgs e)
    {
        string target;
        using (var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择新的数据目录（建议放在非系统盘；选空文件夹会直接用它，否则会在里面建 Wantodo）",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        })
        {
            if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            var selected = dialog.SelectedPath;
            target = IsEmptyDirectory(selected)
                ? selected
                : Path.Combine(selected, "Wantodo");
        }

        var confirm = MessageBox.Show(
            this,
            $"把数据从现在的位置迁移到：\n{target}\n\n当前：{_paths.Root}\n\n" +
            "旧目录不会被删除，只会写一个 .migrated.json 标记。",
            "迁移数据目录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (!_main.MigrateDataTo(target, out var error))
        {
            MessageBox.Show(this, $"迁移没有完成：{error}", "wantodo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var restart = MessageBox.Show(
            this,
            "迁移完成。需要重启才能使用新位置，现在重启吗？",
            "wantodo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (restart == MessageBoxResult.Yes)
        {
            // 只置标志不够：主窗口不会自己关闭，用户会以为「说了重启却什么都没发生」。
            App.RequestRestart();
            Close();
            Application.Current.Shutdown();
            return;
        }

        Close();
    }

    private void OnCleanLegacyClick(object sender, RoutedEventArgs e)
    {
        var legacy = Storage.DataRootService.LegacyDirectory;
        var confirm = MessageBox.Show(
            this,
            $"删除旧数据目录？\n{legacy}\n\n里面的便签数据已经迁移过了，删掉不可恢复。",
            "清理旧数据",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (_main.CleanLegacyData(out var message))
        {
            CleanLegacyButton.IsEnabled = false;
            MessageBox.Show(this, "已清理。", "wantodo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, message ?? "清理失败。", "wantodo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsEmptyDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).GetEnumerator().MoveNext();
        }
        catch (Exception)
        {
            return false;
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
