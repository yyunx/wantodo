using System;
using System.Windows;
using System.Windows.Threading;

namespace DesktopLayerProbe;

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

        app.Run(new ProbeWindow());
    }
}
