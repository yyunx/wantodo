using System;
using System.Windows.Threading;
using Wantodo.App.Interop;
using Wantodo.App.ViewModels;
using Wantodo.Core;

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
        _hook.MouseMoved += OnGlobalMouseMoved;
        _hook.LeftButtonUp += OnGlobalLeftButtonUp;
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
            _hook.MouseMoved -= OnGlobalMouseMoved;
            _hook.LeftButtonUp -= OnGlobalLeftButtonUp;
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

        // 面板自己不抢焦点，WPF 的鼠标捕获在未激活窗口上并不可靠；
        // 拖动与缩放都改由全局钩子驱动。先判边缘（Phase 19），再判 12 DIP 把手带。
        if (_panel.HitTestEdge(x, y, out PanelEdge edge))
        {
            _panel.BeginResize(edge, x, y);
            return;
        }

        if (_panel.HitTestHandle(x, y))
        {
            _panel.BeginDrag(y);
            return;
        }

        // 分隔条 6 DIP > 栏头 26 DIP 拖动区：先判分隔条，避免贴边时两边抢。
        // 画布模式下布局宿主是 Collapsed 的，这两类命中没有意义，直接跳过。
        if (_panel.IsDockMode)
        {
            var screenPoint = new System.Windows.Point(x, y);
            if (_panel.TryBeginSplitDrag(screenPoint))
            {
                return;
            }

            if (_panel.TryBeginPaneDrag(screenPoint, out _))
            {
                return;
            }
        }

        // 下拉框的弹出层是独立 HWND，点它里面的选项会落在面板矩形外；
        // 不能因为这一下把整个面板收起来，否则选列等于关窗。
        if (_panel.IsPickerOpen)
        {
            return;
        }

        if (!_panel.ContainsScreenPoint(x, y))
        {
            _panel.Collapse();
        }
    }

    private void OnGlobalMouseMoved(int x, int y)
    {
        if (_panel is null)
        {
            return;
        }

        if (_panel.IsResizing)
        {
            _panel.ResizeTo(x, y);
            return;
        }

        if (_panel.IsSplitDragging)
        {
            _panel.DragSplit(new System.Windows.Point(x, y));
            return;
        }

        if (_panel.IsPaneDragging)
        {
            _panel.DragPane(new System.Windows.Point(x, y));
            return;
        }

        if (_panel.IsDragging)
        {
            _panel.DragTo(y);
        }
    }

    private void OnGlobalLeftButtonUp(int x, int y)
    {
        if (_panel is null)
        {
            return;
        }

        if (_panel.IsResizing)
        {
            _panel.EndResize();
            return;
        }

        if (_panel.IsSplitDragging)
        {
            _panel.EndSplitDrag();
            return;
        }

        if (_panel.IsPaneDragging)
        {
            _panel.EndPaneDrag();
            return;
        }

        if (_panel.IsDragging)
        {
            _panel.EndDrag();
        }
    }

    public void Dispose()
    {
        Stop();
        _viewModel.TopPanelVisibilityChanged -= _visibilityHandler;
    }
}
