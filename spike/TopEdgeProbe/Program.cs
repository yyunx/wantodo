using System;
using System.Windows;

namespace TopEdgeProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ProbeLog.Error("未处理异常：" + e.ExceptionObject);

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };

        app.DispatcherUnhandledException += (_, e) =>
        {
            ProbeLog.Error("Dispatcher 异常：" + e.Exception);
            e.Handled = true;
        };

        ProbeLog.Section("TopEdgeProbe 启动");
        app.Run(new MainWindow());
    }
}
