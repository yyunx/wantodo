using System;
using System.IO;

namespace TopEdgeProbe;

/// <summary>探针日志：%TEMP%\wantodo-topedge-probe.log，不引入任何日志库。</summary>
internal static class ProbeLog
{
    private static readonly object Gate = new();

    public static string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wantodo-topedge-probe.log");

    static ProbeLog()
    {
        try
        {
            File.WriteAllText(
                Path,
                "# wantodo top-edge probe — " + DateTimeOffset.Now.ToString("O") + Environment.NewLine);
        }
        catch
        {
            // 日志写不了也不能让探针死掉
        }
    }

    public static void Write(string tag, string message)
    {
        var line = DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " [" + tag + "] " + message;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        Console.WriteLine(line);
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERR ", message);

    public static void Section(string title) => Write("====", title);
}
