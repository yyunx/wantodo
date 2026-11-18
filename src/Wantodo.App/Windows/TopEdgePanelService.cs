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
        // 这行打点用来区分「点击根本没到面板」与「到了面板但被画板吃掉」——
        // 钩子在事件路由之前跑，是唯一能看到每一次真实按下的地方。
        var captured = System.Windows.Input.Mouse.Captured;
        var inside = _panel.ContainsScreenPoint(x, y);
        // 弹出层（下拉框/颜色面板/右键菜单/ToolTip）与模态对话框都靠捕获判定「点了外面」，
        // 这一下很可能正落在它自己的选项上：既不能清捕获，也不能当成「点了面板外」。
        // 待办卡里的栏目下拉是 WPF 临时建的 PopupRoot，不属于面板自己的两个 Popup，
        // 只能靠「捕获者是不是弹出层」来认。
        var popupUp = _panel.IsAnyPopupOpen || _panel.IsModalOpen || PopupCapture.IsPopup(captured);
        AppLog.Info($"[指针] 全局按下 screen=({x},{y}) 面板内={inside} 捕获={captured?.GetType().Name ?? "无"} 弹出层={popupUp} 窗口={NativeMethods.DescribeWindowAt(x, y)}");
        if (captured is not null && !popupUp)
        {
            // 自愈：钩子先于 WPF 路由，此刻还挂着的捕获一定是上一轮手势泄漏来的。
            // 不放掉它，这一下点击就被那个元素吞掉；画板在矩形工具下又会把吞掉的点击
            // 当成零尺寸图形静默丢弃，于是「工具栏点不动」且日志一片空白。
            //
            // 但 Popup（颜色面板/列选择）正是靠捕获来判定「点了外面」的：
            // 这时候清捕获等于替用户点了一下外面，面板会在按下的瞬间关掉，
            // 色块永远收不到那一次点击。
            System.Windows.Input.Mouse.Capture(null);
            AppLog.Warn($"[指针] 释放泄漏的鼠标捕获（{captured.GetType().Name}）");
        }

        // 面板不是置顶窗口：只要别的普通窗口压在它上面，那一带的点击就永远不会到面板，
        // 表现正是「面板看得见、点哪儿都没反应，只有点面板外还能靠钩子收起」。
        // 这里先认出被谁压住，再把自己提回最上层，下一次点击就能落回来。
        if (inside && !NativeMethods.WindowAtBelongsToCurrentProcess(x, y))
        {
            AppLog.Warn($"[指针] 面板被别的窗口压住，点击落到了 {NativeMethods.DescribeWindowAt(x, y)}；已把面板提到最上层");
            _panel.EnsureAboveWindows();
        }
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

        // 下拉框/颜色面板的弹出层是独立 HWND，点它里面的选项会落在面板矩形外；
        // 不能因为这一下把整个面板收起来，否则选列、改色等于关窗。
        if (popupUp || PopupCapture.IsPopup(System.Windows.Input.Mouse.Captured))
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
