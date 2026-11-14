using System;
using System.Windows.Threading;
using Wantodo.App.Interop;
using Wantodo.App.ViewModels;

namespace Wantodo.App.Windows;

/// <summary>
/// 顶边下拉面板的桌面层服务：开关打开时建热区 + 钩子，关闭时全部拆掉。
/// 面板本身懒创建——没人撞顶就不占内存（Phase 17 验收：关闭后无窗口、无钩子）。
/// </summary>
public sealed class TopEdgePanelService : IDisposable
{
    private const double HandleDwellMilliseconds = 120;

    private readonly BoardViewModel _viewModel;
    private readonly EventHandler _visibilityHandler;
    private HotZoneWindow? _hotZone;
    private TopPanelWindow? _panel;
    private LowLevelMouseHook? _hook;
    private ForegroundWatcher? _foreground;
    private DispatcherTimer? _dwell;

    public TopEdgePanelService(BoardViewModel viewModel)
    {
        _viewModel = viewModel;
        _visibilityHandler = (_, _) => Sync();
        _viewModel.TopPanelVisibilityChanged += _visibilityHandler;
    }

    /// <summary>按当前开关状态启停；App 启动和设置页切换都会调。</summary>
    public void Sync()
    {
        if (_viewModel.ShowTopPanel)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_hotZone is not null)
        {
            return;
        }

        _dwell = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(HandleDwellMilliseconds),
        };
        _dwell.Tick += OnDwellElapsed;

        _hotZone = new HotZoneWindow();
        _hotZone.MouseEnter += OnHotZoneEnter;
        _hotZone.MouseLeave += OnHotZoneLeave;
        _hotZone.Show();

        _hook = new LowLevelMouseHook();
        _hook.LeftButtonDown += OnGlobalLeftButtonDown;
        if (!_hook.Start())
        {
            AppLog.Warn("顶边下拉面板已打开，但低级鼠标钩子没装上：点面板外不会自动收起");
        }

        _foreground = new ForegroundWatcher();
        _foreground.CoversChanged += OnCoversChanged;
        _foreground.Start();
        if (_foreground.Covers)
        {
            OnCoversChanged(true);
        }

        AppLog.Info("顶边下拉面板已启用（主屏顶边 2 DIP）");
    }

    private void Stop()
    {
        if (_hotZone is null)
        {
            return;
        }

        _dwell?.Stop();
        _dwell = null;

        if (_hotZone is not null)
        {
            _hotZone.MouseEnter -= OnHotZoneEnter;
            _hotZone.MouseLeave -= OnHotZoneLeave;
            _hotZone.Close();
            _hotZone = null;
        }

        if (_hook is not null)
        {
            _hook.LeftButtonDown -= OnGlobalLeftButtonDown;
            _hook.Dispose();
            _hook = null;
        }

        if (_foreground is not null)
        {
            _foreground.CoversChanged -= OnCoversChanged;
            _foreground.Dispose();
            _foreground = null;
        }

        if (_panel is not null)
        {
            _panel.Close();
            _panel = null;
        }

        AppLog.Info("顶边下拉面板已关闭：热区与钩子都已释放");
    }

    private void OnHotZoneEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _dwell?.Stop();
        _dwell?.Start();
    }

    private void OnHotZoneLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _dwell?.Stop();
    }

    private void OnDwellElapsed(object? sender, EventArgs e)
    {
        _dwell?.Stop();

        _panel ??= new TopPanelWindow(_viewModel);
        _panel.ShowHandle();
    }

    private void OnCoversChanged(bool covers)
    {
        // 全屏应用（视频/游戏/无边框全屏）在前台时热区要让路，
        // 否则鼠标点屏幕最上 2 px 会被这块看不见的条吃掉。
        if (covers)
        {
            _hotZone?.Hide();
            if (_panel is not null && _panel.IsVisible)
            {
                _panel.Collapse();
            }

            return;
        }

        _hotZone?.Show();
    }

    private void OnGlobalLeftButtonDown(int x, int y)
    {
        if (_panel is null || !_panel.IsVisible)
        {
            return;
        }

        if (!_panel.ContainsScreenPoint(x, y))
        {
            _panel.Collapse();
        }
    }

    public void Dispose()
    {
        Stop();
        _viewModel.TopPanelVisibilityChanged -= _visibilityHandler;
    }
}
