using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Wantodo.App.Interop;
using Wantodo.Core;

namespace Wantodo.App;

public partial class App : Application
{
    private JsonBoardStore _store = null!;
    private Stopwatch? _startupWatch;
    private Mutex? _singleInstance;
    private Wantodo.App.Windows.TopEdgePanelService? _topPanel;

    /// <summary>必须和 MainWindow.xaml 里的 Title 一致，第二个实例靠它找已有窗口。</summary>
    private const string MainWindowTitle = "wantodo";

    protected override void OnStartup(StartupEventArgs e)
    {
        _startupWatch = Stopwatch.StartNew();

        // 桌面常驻程序最怕「一声不响就没了」：任何未处理异常都要先落日志。
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("UI 线程未处理异常（已拦截，程序继续运行）", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("后台线程未处理异常（进程即将退出）", args.ExceptionObject as Exception);

        base.OnStartup(e);

        // 形式感资源的兜底默认值；ThemeService.Apply 会用当前主题覆盖。
        Resources["ThemeStrokeWidth"] = 0d;
        Resources["ThemeStrokeThickness"] = new System.Windows.Thickness(0);
        Resources["ThemeStrokeBrush"] = System.Windows.Media.Brushes.Transparent;
        Resources["ThemeTextureBrush"] = System.Windows.Media.Brushes.Transparent;
        Resources["ThemeCornerRadius"] = new System.Windows.CornerRadius(10);
        Resources["ThemeCornerRadiusDouble"] = 10d;
        Resources["ThemeElevation"] = 0;
        Resources["ThemeShadowBrush"] = System.Windows.Media.Brushes.Transparent;
        Resources["ThemeGlowVisible"] = System.Windows.Visibility.Collapsed;
        Resources["ThemeShadowOffset"] = 0d;
        Resources["ThemeCardAlpha"] = 1d;

        // 单实例：第二次双击 exe 不该开出第二个窗口（两个实例同时写 data.json 会互相覆盖）。
        // 这里用的是 Local\ 命名空间（只影响当前登录会话），注销后自动释放。
        //
        // 便携副本（目录里有 portable.txt）用自己的命名空间：便携版的意义就是「插上 U 盘就能跑」，
        // 它跟机器上已经装好的那份是两个数据根，互相不该顶掉。否则双击便携 exe 只会把
        // 已安装的那份叫到前台，看起来像「新版本毫无变化」——这条踩过。
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName(), out var createdNew);
        if (!createdNew)
        {
            var existing = NativeMethods.FindWindow(null, MainWindowTitle);
            if (existing != IntPtr.Zero)
            {
                NativeMethods.PostMessage(existing, (uint)NativeMethods.ShowWantodoMessage, IntPtr.Zero, IntPtr.Zero);
            }

            Shutdown();
            return;
        }

        // 数据根：解析 → 建目录 → 拿锁 → 导入 v0.4 数据 → 清理过期文件。必须在读板数据之前。
        Wantodo.App.Storage.DataRootService.Initialize();
        AppLog.Initialize(Wantodo.App.Storage.DataRootService.Paths.LogsDir);

        var resolution = Wantodo.App.Storage.DataRootService.Resolution;
        AppLog.Info(
            $"数据根 {resolution.Root}（来源 {resolution.Source}）" +
            (resolution.Warning is null ? string.Empty : $" ⚠ {resolution.Warning}"));
        if (Wantodo.App.Storage.DataRootService.LastLockMessage is { } lockMessage)
        {
            AppLog.Warn(lockMessage);
        }

        if (Wantodo.App.Storage.DataRootService.ImportLegacyBoard() is { } importMessage)
        {
            AppLog.Info(importMessage);
        }

        var pruned = Wantodo.App.Storage.DataRootService.Prune();
        if (pruned.Logs > 0 || pruned.Trash > 0)
        {
            AppLog.Info($"清理过期文件：日志 {pruned.Logs} 个、回收站 {pruned.Trash} 个");
        }

        _store = new JsonBoardStore(Wantodo.App.Storage.DataRootService.Paths.BoardFile);
        var result = _store.Load();

        AppLog.Info(
            $"启动 data={_store.DataPath} status={result.Status} columns={result.Board.Columns.Count} " +
            $"surface={result.Board.Surface}" + (result.Message is null ? string.Empty : $" msg={result.Message}"));

        foreach (var diagnostic in _store.Diagnostics)
        {
            AppLog.Warn(diagnostic);
        }

        // P2.2：启动时对一次账——没有任何图元引用的笔迹/图片挪进回收目录（D23/D27 的欠账）。
        // 只挪不删，且挪走的东西仍能从 trash/assets-<ts>/ 里翻回来。
        var cleaned = AssetCleanup.Run(
            result.Board.Canvas,
            Wantodo.App.Storage.DataRootService.Paths.CanvasInkDir,
            Wantodo.App.Storage.DataRootService.Paths.CanvasImagesDir,
            Wantodo.App.Storage.DataRootService.Paths.TrashDir);
        if (cleaned.MovedFiles > 0)
        {
            AppLog.Info($"[资产清理] 孤儿文件 {cleaned.MovedFiles} 个（{cleaned.MovedBytes} B）已挪进回收目录");
        }

        foreach (var error in cleaned.Errors)
        {
            AppLog.Warn(error);
        }

        var window = new MainWindow(result.Board, _store, Wantodo.App.Storage.DataRootService.Paths);
        MainWindow = window;
        window.ContentRendered += OnFirstFrameRendered;
        window.Show();

        // 顶边下拉面板：默认关；开关打开才建热区与钩子（Phase 17 验收要求关闭时零开销）。
        _topPanel = new Wantodo.App.Windows.TopEdgePanelService(window.ViewModel);
        _topPanel.Sync();
    }

    /// <summary>启动耗时 + 内存：这两个数字每个阶段都要留档，见 docs/verify/phase3.md。</summary>
    private void OnFirstFrameRendered(object? sender, EventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.ContentRendered -= OnFirstFrameRendered;
        }

        _startupWatch?.Stop();
        using var process = Process.GetCurrentProcess();
        AppLog.Info(
            $"首帧 { _startupWatch?.ElapsedMilliseconds ?? 0 }ms；" +
            $"内存 WS={process.WorkingSet64 / 1024 / 1024}MB private={process.PrivateMemorySize64 / 1024 / 1024}MB；" +
            $"线程={process.Threads.Count}");
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // 注销/关机时也强制落盘一次（OnClosing 不一定来得及）
        if (Windows.Count > 0 && Windows[0] is MainWindow window)
        {
            window.FlushPendingSave();
        }

        base.OnSessionEnding(e);
    }

    private static bool _restartRequested;

    /// <summary>
    /// 单实例名。安装版沿用固定名字（双击第二次只会把第一个叫到前台）；
    /// 便携副本按所在目录派生，各自独立——便携版和安装版是两个数据根，不该互相顶掉。
    /// </summary>
    private static string SingleInstanceName()
    {
        const string defaultName = @"Local\Wantodo.SingleInstance";
        string baseDir;
        try
        {
            baseDir = AppContext.BaseDirectory;
        }
        catch (Exception)
        {
            return defaultName;
        }

        if (!System.IO.File.Exists(System.IO.Path.Combine(baseDir, DataRootResolver.PortableMarkerName)))
        {
            return defaultName;
        }

        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(baseDir.ToUpperInvariant()));
        return $@"Local\Wantodo.Portable.{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }

    /// <summary>迁移完数据后要求重启：退出时由 cmd 起一个新进程，避开单实例互斥。</summary>
    public static void RequestRestart() => _restartRequested = true;

    protected override void OnExit(ExitEventArgs e)
    {
        _topPanel?.Dispose();
        Storage.DataRootService.Shutdown();

        if (_restartRequested)
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c start \"\" \"{exe}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    });
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("重启失败", ex);
            }
        }

        base.OnExit(e);
    }
}