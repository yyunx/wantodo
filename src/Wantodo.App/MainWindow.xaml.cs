using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wantodo.App.Interop;
using Wantodo.App.Shell;
using Wantodo.App.ViewModels;
using Wantodo.App.Appearance;
using Wantodo.Core;

namespace Wantodo.App;

public partial class MainWindow : Window
{
    private const string NoteDataFormat = "wantodo/note";

    /// <summary>一栏能压到的最窄宽度（DIP）。再窄就横向滚动，不让文字被剪成一条缝。</summary>
    private const double MinColumnWidth = LayoutMetrics.MinPaneWidth;

    /// <summary>栏与栏之间的间距：模板里是 Margin="4,0"，左右各 4。</summary>
    private const double ColumnGap = LayoutMetrics.PaneGap;

    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(MinColumnWidth));

    private readonly BoardViewModel _viewModel;

    private IntPtr _hwnd;
    private ShellRestartWatcher? _shellRestartWatcher;
    private HwndSource? _hwndSource;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private QuickCaptureWindow? _quickCapture;
    private bool _hotKeyRegistered;

    private bool _applyingSize;
    private double _expectedWidth = double.NaN;
    private bool _manualSizeLogged;

    private Point _dragStart;
    private NoteViewModel? _draggingNote;

    public MainWindow(Board board, JsonBoardStore store)
    {
        // 数据里一栏都没有时界面会是空的，给个最小可用的占位栏（不改用户的 JSON，只在内存里补）
        if (board.Columns.Count == 0)
        {
            board.Columns.Add(new NoteColumn { Title = "未命名栏" });
            AppLog.Warn("数据里没有栏，已临时补一栏");
        }

        InitializeComponent();

        // 启动时就要套用存下来的外观；只靠设置面板改主题的话，重启后又会退回默认深色
        // （实测漏过这一步，见 docs/verify/phase4.md）。
        ThemeService.Apply(board);

        _viewModel = new BoardViewModel(board, store);
        DataContext = _viewModel;

        _viewModel.Columns.CollectionChanged += OnColumnsChanged;
        Loaded += (_, _) => UpdateColumnWidths();
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;

        ApplyPlacement();

        // AutoFit 下，上次关窗时的宽度可能对不上现在恢复出来的栏数（比如加了栏还没关就被杀），
        // 启动时先收敛一次，免得一睁眼就是被挤扁的四栏。
        ApplyAutoFit("启动");
    }

    /// <summary>每栏宽度，由窗口可用宽度和栏数算出来；窄到放不下就保持最小宽度并横向滚动。</summary>
    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    private Board Board => _viewModel.Model;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        AppLog.Info($"窗口就绪 hwnd=0x{_hwnd.ToInt64():X} {Width}×{Height} DIP @ {Left},{Top}");

        DesktopLayer.TryRoundCorners(_hwnd);

        if (!DesktopLayer.Attach(_hwnd, Board.Surface))
        {
            AppLog.Warn($"没能挂到 {Board.Surface} 层；" + DesktopLayer.Describe(_hwnd));
        }

        if (string.Equals(Board.Surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase))
        {
            _shellRestartWatcher = ShellRestartWatcher.Install(_hwnd, OnShellRestarted);
        }

        HookSystemEvents();

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        _hotKeyRegistered = NativeMethods.RegisterHotKey(
            _hwnd,
            NativeMethods.QuickCaptureHotKeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_SPACE);

        if (_hotKeyRegistered)
        {
            AppLog.Info("已注册全局快捷键 Ctrl+Alt+Space（快速记一条）");
        }
        else
        {
            AppLog.Warn($"全局快捷键 Ctrl+Alt+Space 注册失败 err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}（多半被别的程序占用）");
        }

        _trayIcon = new TrayIcon(this, _viewModel);
        AppLog.Info("托盘图标已创建");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        UnhookSystemEvents();

        if (_hotKeyRegistered && _hwnd != IntPtr.Zero)
        {
            _ = NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.QuickCaptureHotKeyId);
            _hotKeyRegistered = false;
        }

        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

        _quickCapture?.Close();
        _settingsWindow?.Close();

        _shellRestartWatcher?.Dispose();
        _shellRestartWatcher = null;

        FlushPendingSave();

        base.OnClosing(e);
    }

    /// <summary>托盘左键：显示 / 隐藏。显示时不抢焦点（桌面物件不该把用户正在打的字打断）。</summary>
    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            AppLog.Info("托盘：隐藏窗口");
            return;
        }

        Show();

        if (string.Equals(Board.Surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase))
        {
            _ = DesktopLayer.Attach(_hwnd, Board.Surface);
        }

        NativeMethods.ShowWithoutActivating(_hwnd);
        AppLog.Info("托盘：显示窗口（不夺焦点）");
    }

    /// <summary>切换桌面层级：立即生效、写配置，并同步维护 Explorer 重启监听。</summary>
    public void SetSurface(string surface)
    {
        var ok = DesktopLayer.Attach(_hwnd, surface);
        Board.Surface = surface;
        _viewModel.RequestSave();
        ManageShellWatcher();
        _trayIcon?.Refresh();
        AppLog.Info($"层级切换为 {surface}（{(ok ? "成功" : "失败，已记录日志")}）");
    }

    public void SetTheme(string theme)
    {
        _viewModel.SetTheme(theme);
        _trayIcon?.Refresh();
        AppLog.Info($"主题切换为 {theme}");
    }

    public void SetAutostart(bool enabled)
    {
        _trayIcon?.Refresh();
        AppLog.Info($"开机自启 = {enabled}");
    }

    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this, _viewModel);
        _settingsWindow.Show();
        AppLog.Info("打开设置面板");
    }

    public void OnSettingsClosed()
    {
        _settingsWindow = null;
        _trayIcon?.Refresh();
    }

    /// <summary>全局快捷键唤出「快速记一条」；没有活动栏时退到第一栏。</summary>
    public void ShowQuickCapture()
    {
        if (_quickCapture is not null)
        {
            _quickCapture.Activate();
            return;
        }

        var target = _viewModel.ActiveColumn ?? _viewModel.Columns.FirstOrDefault();
        if (target is null)
        {
            AppLog.Warn("快速记一条：当前没有任何栏，忽略");
            return;
        }

        _quickCapture = new QuickCaptureWindow(target);
        _quickCapture.Closed += (_, _) => _quickCapture = null;
        _quickCapture.Show();
    }

    private void ManageShellWatcher()
    {
        var isDesktop = string.Equals(Board.Surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase);

        if (isDesktop && _shellRestartWatcher is null)
        {
            _shellRestartWatcher = ShellRestartWatcher.Install(_hwnd, OnShellRestarted);
        }
        else if (!isDesktop && _shellRestartWatcher is not null)
        {
            _shellRestartWatcher.Dispose();
            _shellRestartWatcher = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == (int)NativeMethods.WM_HOTKEY && wParam.ToInt64() == NativeMethods.QuickCaptureHotKeyId)
        {
            ShowQuickCapture();
            handled = true;
        }
        else if (message == NativeMethods.ShowWantodoMessage)
        {
            // 用户又双击了一次 exe：不新开窗口，把已有的这扇叫到前面（单实例，见 docs/verify/phase5.md）
            BringToFront();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 把窗口叫到最前面一次。桌面层窗口直接 <c>SetForegroundWindow</c> 会被系统拒绝，
    /// 所以先临时置顶、抢到前台后再撤掉置顶，让它落回桌面层。
    /// </summary>
    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
            if (Board.Surface == DesktopLayer.SurfaceDesktop)
            {
                DesktopLayer.Attach(_hwnd, Board.Surface);
            }
        }

        var topmost = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOPMOST;
        if (topmost == 0)
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        }

        NativeMethods.SetForegroundWindow(_hwnd);
        Activate();

        if (topmost == 0)
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
        }

        AppLog.Info("第二次启动：已把已有窗口叫到前面");
    }

    /// <summary>把窗口位置写回模型并立刻落盘。关窗、注销、关机都会走这里。</summary>
    public void FlushPendingSave()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        _viewModel.SavePlacement(Left, Top, width, height);
        _viewModel.FlushSave();
        AppLog.Info($"已保存布局 {width}×{height} @ {Left},{Top}；" + MemorySnapshot());
    }

    private void OnShellRestarted()
    {
        var ok = DesktopLayer.Attach(_hwnd, Board.Surface);
        AppLog.Info($"Explorer 重启后重挂 {(ok ? "成功" : "失败")}；" + DesktopLayer.Describe(_hwnd));
    }

    private void ApplyPlacement()
    {
        var placement = Board.Placement;
        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);
        // 记下「我们自己设的宽度」，SizeChanged 里靠它区分用户拖拽
        _expectedWidth = Width;

        if (placement.HasPosition)
        {
            Left = placement.Left!.Value;
            Top = placement.Top!.Value;
        }
        else
        {
            // 首次启动放到主屏右上角，像手机小组件的习惯位置
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 48;
            Top = area.Top + 48;
            AppLog.Info($"首次启动，默认落点 {Left},{Top}");
        }

        ClampIntoVirtualScreen();
    }

    /// <summary>
    /// 显示器被拔掉 / 分辨率变了以后，别让窗口留在屏幕外找不回来。
    /// 只在「整个窗口已经不在虚拟桌面内」时才动手：用户自己把便签卡在屏幕边缘是允许的。
    /// 拉回的目标是主屏工作区而不是虚拟桌面，否则会藏到任务栏底下。
    /// </summary>
    private void ClampIntoVirtualScreen()
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        if (PlacementGuard.IsFullyInside(
                Left, Top, Width, Height, virtualLeft, virtualTop, virtualRight, virtualBottom))
        {
            return;
        }

        var work = SystemParameters.WorkArea;
        var (left, top) = PlacementGuard.Clamp(
            Left, Top, Width, Height, work.Left, work.Top, work.Right, work.Bottom);

        if (Math.Abs(left - Left) > 0.5 || Math.Abs(top - Top) > 0.5)
        {
            AppLog.Warn($"保存的位置 {Left},{Top} 在屏幕外，已拉回到 {left},{top}");
            Left = left;
            Top = top;
        }
    }

    // ---------- 系统事件：锁屏解锁 / 改分辨率 / 休眠唤醒 后重新贴回桌面层 ----------

    private bool _systemEventsHooked;

    private void HookSystemEvents()
    {
        if (_systemEventsHooked)
        {
            return;
        }

        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _systemEventsHooked = true;
    }

    private void UnhookSystemEvents()
    {
        if (!_systemEventsHooked)
        {
            return;
        }

        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _systemEventsHooked = false;
    }

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is not (Microsoft.Win32.SessionSwitchReason.SessionUnlock
            or Microsoft.Win32.SessionSwitchReason.ConsoleConnect
            or Microsoft.Win32.SessionSwitchReason.RemoteConnect))
        {
            return;
        }

        RunOnUi(nameof(OnSessionSwitch), () => ReattachDesktopLayer($"解锁（{e.Reason}）"));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => RunOnUi(nameof(OnDisplaySettingsChanged), () =>
        {
            ClampIntoVirtualScreen();
            UpdateColumnWidths();
            ReattachDesktopLayer("显示设置变化");
        });

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume)
        {
            return;
        }

        RunOnUi(nameof(OnPowerModeChanged), () => ReattachDesktopLayer("从休眠唤醒"));
    }

    private void RunOnUi(string reason, Action action)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(action));
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Error($"处理 {reason} 失败", ex);
        }
    }

    /// <summary>把窗口重新挂到桌面层；只对 Desktop 模式生效（Normal/Topmost 本来就不该被挪）。</summary>
    private void ReattachDesktopLayer(string reason)
    {
        if (!string.Equals(Board.Surface, DesktopLayer.SurfaceDesktop, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var ok = DesktopLayer.Attach(_hwnd, Board.Surface);
        AppLog.Info($"{reason}：重挂桌面层 {(ok ? "成功" : "失败")}；" + DesktopLayer.Describe(_hwnd));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        AppLog.Info($"DPI 变化 {oldDpi.PixelsPerDip:F2} → {newDpi.PixelsPerDip:F2}，重算栏宽");
        UpdateColumnWidths();
    }

    private static string MemorySnapshot()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"内存 WS={process.WorkingSet64 / 1024 / 1024}MB private={process.PrivateMemorySize64 / 1024 / 1024}MB";
    }

    // ---------- 顶栏 ----------

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标已经松开时 DragMove 会抛；拖拽而已，忽略
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ---------- 栏 ----------

    /// <summary>记住「现在在哪一栏操作」，新栏就插在它右边（见 decisions.md D9）。</summary>
    private void OnColumnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColumnViewModel column })
        {
            _viewModel.SetActiveColumn(column);
        }
    }

    /// <summary>点栏里任意位置也算「在这一栏操作」，比只认键盘焦点稳。</summary>
    private void OnColumnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ColumnViewModel column })
        {
            _viewModel.SetActiveColumn(column);
        }
    }

    /// <summary>
    /// 栏宽 = 可用宽度平均分；分到比 <see cref="MinColumnWidth"/> 还窄就停在最小宽度，
    /// 让外层横向滚动顶出去，而不是把每栏压成一条缝。
    /// </summary>
    private void UpdateColumnWidths()
    {
        int count = _viewModel.Columns.Count;
        if (count <= 0)
        {
            return;
        }

        double available = ColumnsScroller.ActualWidth;
        if (available <= 0)
        {
            available = ActualWidth - 16;
        }

        double width = (available - ColumnGap * count) / count;
        ColumnWidth = Math.Max(MinColumnWidth, width);
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                ApplyAutoFit("加栏");
                break;

            case NotifyCollectionChangedAction.Remove:
                ApplyAutoFit("删栏");
                break;
        }

        UpdateColumnWidths();
    }

    /// <summary>
    /// 结构变了（加栏 / 删栏）就让窗口收放到「刚好放下这些栏」。
    /// 只在 <see cref="SizeModes.AutoFit"/> 下生效；用户拖过边框就切到 Manual，不再擅自改尺寸。
    /// 目标宽度 = 16 + n × (280 + 8)；夹在 MINWIDTH 与工作区宽度之间。
    /// </summary>
    public void ApplyAutoFit(string reason)
    {
        int count = _viewModel.Columns.Count;
        if (count <= 0)
        {
            return;
        }

        if (!string.Equals(Board.Placement.SizeMode, SizeModes.AutoFit, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var work = SystemParameters.WorkArea;
        double target = Math.Max(MinWidth, LayoutMetrics.FitTo(LayoutMetrics.ComputePreferredWidth(count), count, work.Width));
        double current = ActualWidth > 0 ? ActualWidth : Width;

        if (Math.Abs(target - current) < 0.5)
        {
            _expectedWidth = current;
            return;
        }

        // 默认保持左边缘不动；只有右边越界时才整体左移，避免整窗跳一下
        double left = Left;
        if (left + target > work.Right)
        {
            left = Math.Max(work.Left, work.Right - target);
        }

        _applyingSize = true;
        try
        {
            Left = left;
            Width = target;
        }
        finally
        {
            _applyingSize = false;
            _expectedWidth = Width;
        }

        AppLog.Info($"AutoFit（{reason}）：{current:F0} → {Width:F0} DIP（{count} 栏）@ {left:F0}");

        if (Math.Abs(Left - left) > 0.5 || Math.Abs(Width - target) > 0.5)
        {
            AppLog.Warn($"AutoFit 目标 {target:F0} 被系统夹到 {Width:F0}（MinWidth={MinWidth}）");
        }
    }

    /// <summary>设置面板切「跟随栏数 / 固定」。</summary>
    public void SetSizeMode(string mode, string reason)
    {
        string normalized = SizeModes.Normalize(mode);
        if (string.Equals(Board.Placement.SizeMode, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Board.Placement.SizeMode = normalized;
        _viewModel.RequestSave();
        AppLog.Info($"窗口尺寸策略切到 {normalized}（{reason}）");

        if (string.Equals(normalized, SizeModes.AutoFit, StringComparison.OrdinalIgnoreCase))
        {
            // 切回自动时立刻收敛一次，不要让用户还要再点一下才看到效果
            ApplyAutoFit("切回跟随栏数");
        }
        else
        {
            _expectedWidth = Width;
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateColumnWidths();

        if (!IsLoaded || WindowState != WindowState.Normal || _applyingSize)
        {
            return;
        }

        // 我们自己设的宽度不算「用户拖拽」：比较的是上一次主动设置的宽度
        if (!double.IsNaN(_expectedWidth) && Math.Abs(Width - _expectedWidth) < 0.5)
        {
            return;
        }

        if (string.Equals(Board.Placement.SizeMode, SizeModes.Manual, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_manualSizeLogged)
        {
            _manualSizeLogged = true;
            AppLog.Info("用户调整了窗口尺寸，切到 Manual（之后加栏/删栏不再自动改宽度）");
        }

        Board.Placement.SizeMode = SizeModes.Manual;
        _viewModel.RequestSave();
        _expectedWidth = Width;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Normal)
        {
            SetSizeMode(SizeModes.Manual, $"窗口状态变为 {WindowState}");
        }
    }

    private void OnColumnTitleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: ColumnViewModel column })
        {
            return;
        }

        column.BeginTitleEdit();
        e.Handled = true;
    }

    private void OnTitleEditBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 折叠状态下也会触发 Loaded，只有真正显示出来才抢焦点
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => FocusAndSelectAll(sender as TextBox)));
        }
    }

    private void OnTitleEditBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ColumnViewModel column })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                column.CommitTitleEdit();
                e.Handled = true;
                break;

            case Key.Escape:
                column.CancelTitleEdit();
                e.Handled = true;
                break;
        }
    }

    private void OnTitleEditBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ColumnViewModel column })
        {
            column.CommitTitleEdit();
        }
    }

    private void OnDeleteColumnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ColumnViewModel column })
        {
            return;
        }

        if (_viewModel.Columns.Count <= 1)
        {
            MessageBox.Show(this, "至少要留一栏。", "删不了", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var noteCount = column.Notes.Count;
        if (noteCount > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"「{column.Title}」里还有 {noteCount} 条，一起删掉？",
                "删除这一栏",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _viewModel.RemoveColumn(column);
    }

    private void OnDraftPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ColumnViewModel column })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                // 回车提交并留在原输入框，可以连着敲下一行
                column.AddDraft();
                e.Handled = true;
                break;

            case Key.Escape:
                column.DraftText = string.Empty;
                e.Handled = true;
                break;
        }
    }

    // ---------- 条目 ----------

    private void OnNoteRowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: NoteViewModel note } row)
        {
            return;
        }

        _draggingNote = note;
        _dragStart = e.GetPosition(this);
        row.Focus();
    }

    private void OnNoteRowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingNote is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var note = _draggingNote;
        _draggingNote = null;

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(NoteDataFormat, note), DragDropEffects.Move);
        }
        finally
        {
            ClearDropMarks();
        }
    }

    private void OnNoteRowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: NoteViewModel note })
        {
            return;
        }

        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _draggingNote = null;
        note.BeginEdit();
        e.Handled = true;
    }

    private void OnNoteEditBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 折叠状态下 Loaded 也会触发，只有真正显示出来才抢焦点
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => FocusAndSelectAll(sender as TextBox)));
        }
    }

    private void OnNoteEditBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: NoteViewModel note } box)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Shift:
                // Shift+Enter 在条目内换行（WPF TextBox 只认不带 Ctrl 的 Enter）
                e.Handled = false;
                break;

            case Key.Enter:
                note.CommitEdit();
                FocusNoteRow(box);
                e.Handled = true;
                break;

            case Key.Escape:
                note.CancelEdit();
                FocusNoteRow(box);
                e.Handled = true;
                break;
        }
    }

    private void OnNoteEditBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: NoteViewModel note })
        {
            note.CommitEdit();
        }
    }

    private void OnDeleteNoteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteViewModel note })
        {
            _viewModel.RemoveNote(note);
        }
    }

    private void OnNotesListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is not ItemsControl list)
        {
            return;
        }

        var row = FindNoteRow(e.OriginalSource as DependencyObject);
        if (row?.DataContext is not NoteViewModel note)
        {
            return;
        }

        var index = IndexOfRow(list, row);

        switch (e.Key)
        {
            case Key.Up:
                FocusRowAt(list, index - 1);
                e.Handled = true;
                break;

            case Key.Down:
                FocusRowAt(list, index + 1);
                e.Handled = true;
                break;

            case Key.Space:
                note.Toggle();
                e.Handled = true;
                break;

            case Key.Enter:
                note.BeginEdit();
                e.Handled = true;
                break;
        }
    }

    // ---------- 拖拽落位 ----------

    private void OnNotesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(NoteDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var resolved = ResolveListAndColumn(sender);
        if (resolved is null)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var (list, column) = resolved.Value;
        var index = ComputeInsertIndex(list, e.GetPosition(list));
        ShowDropMarks(column, index);

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnNotesDragLeave(object sender, DragEventArgs e) => ClearDropMarks();

    private void OnNotesDrop(object sender, DragEventArgs e)
    {
        var resolved = ResolveListAndColumn(sender);

        if (!e.Data.GetDataPresent(NoteDataFormat) ||
            e.Data.GetData(NoteDataFormat) is not NoteViewModel note ||
            resolved is null)
        {
            ClearDropMarks();
            return;
        }

        var (list, column) = resolved.Value;
        var index = ComputeInsertIndex(list, e.GetPosition(list));

        var source = _viewModel.Columns.FirstOrDefault(c => c.Notes.Contains(note));
        var sourceIndex = source?.Notes.IndexOf(note) ?? -1;

        // 同栏内往后拖时，移走会让目标行前移一位，所以要减一
        if (ReferenceEquals(source, column) && sourceIndex >= 0 && index > sourceIndex)
        {
            index--;
        }

        _viewModel.MoveNote(note, column, index);
        ClearDropMarks();
        e.Handled = true;
    }

    private static (ItemsControl List, ColumnViewModel Column)? ResolveListAndColumn(object sender)
    {
        var list = sender as ItemsControl ?? FindDescendant<ItemsControl>(sender as DependencyObject);
        if (list?.DataContext is not ColumnViewModel column)
        {
            return null;
        }

        return (list, column);
    }

    private static int ComputeInsertIndex(ItemsControl list, Point position)
    {
        var index = 0;

        foreach (var item in list.Items)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
            {
                var top = container.TranslatePoint(new Point(0, 0), list).Y;
                if (position.Y < top + container.ActualHeight / 2)
                {
                    return index;
                }
            }

            index++;
        }

        return list.Items.Count;
    }

    private void ShowDropMarks(ColumnViewModel column, int index)
    {
        ClearDropMarks();

        if (index >= column.Notes.Count)
        {
            // 落到末尾时没有「下一行」可以划线，用整栏高亮代替
            column.IsDropTarget = true;
            return;
        }

        column.Notes[index].IsDropBefore = true;
    }

    private void ClearDropMarks()
    {
        foreach (var column in _viewModel.Columns)
        {
            column.IsDropTarget = false;

            foreach (var note in column.Notes)
            {
                note.IsDropBefore = false;
            }
        }
    }

    // ---------- 视觉树小工具 ----------

    private static void FocusAndSelectAll(TextBox? box)
    {
        if (box is null)
        {
            return;
        }

        box.Focus();
        box.SelectAll();
        box.CaretIndex = box.Text.Length;
    }

    private void FocusNoteRow(DependencyObject from)
    {
        var row = FindNoteRow(from);
        row?.Focus();
    }

    private static int IndexOfRow(ItemsControl list, DependencyObject row)
    {
        var container = FindAncestor<ContentPresenter>(row);
        return container is null ? -1 : list.ItemContainerGenerator.IndexFromContainer(container);
    }

    private static void FocusRowAt(ItemsControl list, int index)
    {
        if (index < 0 || index >= list.Items.Count)
        {
            return;
        }

        if (list.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
        {
            FindDescendantByName<FrameworkElement>(container, "NoteRow")?.Focus();
        }
    }

    private static FrameworkElement? FindNoteRow(DependencyObject? start)
    {
        var current = start;

        while (current is not null)
        {
            if (current is FrameworkElement { Name: "NoteRow" } element)
            {
                return element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? start) where T : DependencyObject
    {
        if (start is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(start);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);

            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindDescendantByName<T>(DependencyObject? start, string name) where T : FrameworkElement
    {
        if (start is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(start);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);

            if (child is T match && match.Name == name)
            {
                return match;
            }

            var nested = FindDescendantByName<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
