using System;
using System.IO;

namespace DesktopLayerProbe;

/// <summary>
/// 探针的最小日志：同时写 %TEMP%\wantodo-probe.log 和控制台。
/// 探针里不引入任何日志库（AGENTS.md：第一版不引入第三方依赖）。
/// </summary>
internal static class ProbeLog
{
    private static readonly object Gate = new();

    public static string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wantodo-probe.log");

    static ProbeLog()
    {
        try
        {
            File.WriteAllText(Path, "# wantodo desktop-layer probe — " + DateTimeOffset.Now.ToString("O") + Environment.NewLine);
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

    /// <summary>多行文本按行拆开，前缀沿用同一个 tag，方便 grep。</summary>
    public static void Block(string tag, string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            Write(tag, line);
        }
    }
}
