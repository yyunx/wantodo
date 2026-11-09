using System;
using System.Windows;
using Wantodo.Core;

namespace Wantodo.App;

public partial class App : Application
{
    private JsonBoardStore _store = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
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
        window.Show();
    }
}
