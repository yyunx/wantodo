using System.IO;
using System.Windows;
using Wantodo.App.Interop;
using Wantodo.App.ViewModels;
using Wantodo.Core;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Wantodo.App.Shell;

/// <summary>
/// 托盘图标：左键显示/隐藏，右键菜单切换层级、主题、自启等。
/// 用 <see cref="WinForms.NotifyIcon"/> 而不是手写 Shell_NotifyIcon：
/// 它是 Windows Desktop SDK 自带的（不是第三方 NuGet），并且自带 Explorer 重启后的重注册。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainWindow _window;
    private readonly BoardViewModel _viewModel;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;

    private readonly WinForms.ToolStripMenuItem _toggleItem;
    private readonly WinForms.ToolStripMenuItem _desktopItem;
    private readonly WinForms.ToolStripMenuItem _normalItem;
    private readonly WinForms.ToolStripMenuItem _topmostItem;
    private readonly WinForms.ToolStripMenuItem _darkItem;
    private readonly WinForms.ToolStripMenuItem _lightItem;
    private readonly WinForms.ToolStripMenuItem _hideCompletedItem;
    private readonly WinForms.ToolStripMenuItem _autostartItem;

    public TrayIcon(MainWindow window, BoardViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;

        _toggleItem = new WinForms.ToolStripMenuItem("显示 / 隐藏", null, (_, _) => _window.ToggleVisibility())
        {
            Font = new Drawing.Font(WinForms.SystemInformation.MenuFont, Drawing.FontStyle.Bold),
        };

        _desktopItem = new WinForms.ToolStripMenuItem("桌面层（图标后面）", null, (_, _) => _window.SetSurface(DesktopLayer.SurfaceDesktop));
        _normalItem = new WinForms.ToolStripMenuItem("普通窗口", null, (_, _) => _window.SetSurface(DesktopLayer.SurfaceNormal));
        _topmostItem = new WinForms.ToolStripMenuItem("置顶窗口", null, (_, _) => _window.SetSurface(DesktopLayer.SurfaceTopmost));
        var surfaceMenu = new WinForms.ToolStripMenuItem("层级");
        surfaceMenu.DropDownItems.AddRange([_desktopItem, _normalItem, _topmostItem]);

        _darkItem = new WinForms.ToolStripMenuItem("深色", null, (_, _) => _window.SetTheme(ThemePalette.DarkTheme));
        _lightItem = new WinForms.ToolStripMenuItem("浅色", null, (_, _) => _window.SetTheme(ThemePalette.LightTheme));
        var themeMenu = new WinForms.ToolStripMenuItem("外观");
        themeMenu.DropDownItems.AddRange([_darkItem, _lightItem]);

        _hideCompletedItem = new WinForms.ToolStripMenuItem("隐藏已完成", null, (_, _) =>
            _viewModel.HideCompleted = !_viewModel.HideCompleted);
        _hideCompletedItem.CheckOnClick = false;

        _autostartItem = new WinForms.ToolStripMenuItem("开机自启", null, (_, _) =>
            _window.SetAutostart(Autostart.SetEnabled(!Autostart.IsEnabled())));

        var settingsItem = new WinForms.ToolStripMenuItem("设置…", null, (_, _) => _window.ShowSettings());
        var exitItem = new WinForms.ToolStripMenuItem("退出", null, (_, _) => _window.Close());

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _toggleItem,
            settingsItem,
            new WinForms.ToolStripSeparator(),
            surfaceMenu,
            themeMenu,
            _hideCompletedItem,
            _autostartItem,
            new WinForms.ToolStripSeparator(),
            exitItem,
        ]);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "wantodo — 桌面便签",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        // 左键点击：显示 / 隐藏；双击：直接开设置
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                _window.ToggleVisibility();
            }
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                _window.ShowSettings();
            }
        };

        Refresh();
    }

    /// <summary>菜单里的勾选状态跟着模型走；每次打开菜单前和设置变更后都要刷新。</summary>
    public void Refresh()
    {
        _menu.Opening += (_, _) => RefreshCore();
        RefreshCore();
    }

    private void RefreshCore()
    {
        _desktopItem.Checked = string.Equals(_viewModel.Model.Surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase);
        _normalItem.Checked = string.Equals(_viewModel.Model.Surface, DesktopLayer.SurfaceNormal, StringComparison.OrdinalIgnoreCase);
        _topmostItem.Checked = string.Equals(_viewModel.Model.Surface, DesktopLayer.SurfaceTopmost, StringComparison.OrdinalIgnoreCase);
        _darkItem.Checked = !ThemePalette.IsLight(_viewModel.Model.Theme);
        _lightItem.Checked = ThemePalette.IsLight(_viewModel.Model.Theme);
        _hideCompletedItem.Checked = _viewModel.HideCompleted;
        _autostartItem.Checked = Autostart.IsEnabled();
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("Assets/wantodo.ico", UriKind.Relative));
            if (resource is not null)
            {
                using var stream = new MemoryStream();
                resource.Stream.CopyTo(stream);
                stream.Position = 0;
                return new Drawing.Icon(stream);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"托盘图标加载失败，退回系统图标：{ex.Message}");
        }

        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
