using System;
using System.IO;
using System.Text;

namespace Wantodo.App;

/// <summary>
/// 极简日志：写 <c>&lt;数据根&gt;\logs\app-yyyyMMdd.log</c>（按天滚动，保留 14 天）。
/// 启动早期还没解析出数据根时先写 <c>%APPDATA%\wantodo\app.log</c>，解析完再切过去，
/// 保证「解析本身失败」也有据可查。
/// </summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>当前日志文件路径（未初始化时是旧位置的兜底）。</summary>
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

    /// <summary>数据根就绪后切到 <c>&lt;root&gt;\logs\app-yyyyMMdd.log</c>。</summary>
    public static void Initialize(string logsDirectory)
    {
        var dir = logsDirectory;
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, Core.DataRetention.LogFileName(DateTimeOffset.Now));
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
