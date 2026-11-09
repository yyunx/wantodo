using System;
using System.IO;
using System.Text;

namespace Wantodo.App;

/// <summary>
/// 极简日志：写 %APPDATA%\wantodo\app.log。
/// Phase 1 不引日志库；出问题时用户把 app.log 发过来就能定位，比看任务管理器猜强。
/// </summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string Path
    {
        get
        {
            if (_path is null)
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "wantodo");
                Directory.CreateDirectory(dir);
                _path = System.IO.Path.Combine(dir, "app.log");
            }

            return _path;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERR ", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 日志失败绝不影响主流程
            }
        }
    }
}
