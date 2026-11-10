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

        // 单实例：第二次双击 exe 不该开出第二个窗口（两个实例同时写 data.json 会互相覆盖）。
        // 这里用的是 Local\ 命名空间（只影响当前登录会话），注销后自动释放。
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Wantodo.SingleInstance", out var createdNew);
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

        var window = new MainWindow(result.Board, _store, Wantodo.App.Storage.DataRootService.Paths);
        MainWindow = window;
        window.ContentRendered += OnFirstFrameRendered;
        window.Show();
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

    /// <summary>迁移完数据后要求重启：退出时由 cmd 起一个新进程，避开单实例互斥。</summary>
    public static void RequestRestart() => _restartRequested = true;

    protected override void OnExit(ExitEventArgs e)
    {
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