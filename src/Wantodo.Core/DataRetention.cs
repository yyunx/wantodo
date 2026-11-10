using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Wantodo.Core;

/// <summary>
/// 按时间清理日志与回收站。只删过期的，当天的永远不删。
/// </summary>
public static class DataRetention
{
    public static readonly TimeSpan LogRetention = TimeSpan.FromDays(14);
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(30);

    /// <summary>删除目录下匹配 <paramref name="searchPattern"/> 且最后写入早于期限的文件，返回删除数量。</summary>
    public static int PruneFiles(string directory, string searchPattern, TimeSpan maxAge, DateTimeOffset now)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cutoff = now - maxAge;
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(directory, searchPattern).ToList())
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 删不掉不算错误；下次启动再试。
            }
        }

        return removed;
    }

    /// <summary>清理日志与回收站，返回 (日志删除数, 回收站删除数)。</summary>
    public static (int Logs, int Trash) PruneAll(DataPaths paths, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return (
            PruneFiles(paths.LogsDir, "app-*.log", LogRetention, now),
            PruneFiles(paths.TrashDir, "*", TrashRetention, now));
    }

    /// <summary>日志文件名：<c>app-yyyyMMdd.log</c>（按天滚动）。</summary>
    public static string LogFileName(DateTimeOffset now)
        => $"app-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log";
}
