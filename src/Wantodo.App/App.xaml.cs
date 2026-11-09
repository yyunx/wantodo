using System;
using System.Diagnostics;
using System.Windows;
using Wantodo.Core;

namespace Wantodo.App;

public partial class App : Application
{
    private JsonBoardStore _store = null!;
    private Stopwatch? _startupWatch;

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

        _store = new JsonBoardStore();
        var result = _store.Load();

        AppLog.Info(
            $"启动 data={_store.DataPath} status={result.Status} columns={result.Board.Columns.Count} " +
            $"surface={result.Board.Surface}" + (result.Message is null ? string.Empty : $" msg={result.Message}"));

        foreach (var diagnostic in _store.Diagnostics)
        {
            AppLog.Warn(diagnostic);
        }

        var window = new MainWindow(result.Board, _store);
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
}