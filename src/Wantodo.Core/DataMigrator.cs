using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wantodo.Core;

public sealed record MigrationFile(string RelativePath, long Length);

/// <summary>迁移前检查结果。<see cref="CanMigrate"/> 为 false 时 <see cref="Error"/> 说明原因。</summary>
public sealed record MigrationInspection(
    bool CanMigrate,
    string? Error,
    IReadOnlyList<MigrationFile> Files,
    long TotalBytes);

public sealed record MigrationResult(
    bool Success,
    string? Error,
    string? TargetRoot,
    int FileCount,
    long TotalBytes);

/// <summary>
/// 数据根迁移。状态机：Preflight → Copying（暂存目录）→ Verifying → Switching → Done，
/// 任一步失败都删暂存、指针不动、源数据完好。
/// </summary>
public static class DataMigrator
{
    private const string MigratedMarkerName = ".migrated.json";

    /// <summary>只做检查，不碰磁盘（除枚举文件）。</summary>
    public static MigrationInspection Inspect(string sourceRoot, string targetRoot, long targetFreeBytes)
    {
        var files = new List<MigrationFile>();
        var source = TryNormalize(sourceRoot);
        var target = TryNormalize(targetRoot);

        if (source is null)
        {
            return Fail("源目录路径不合法");
        }

        if (target is null)
        {
            return Fail("目标目录路径不合法");
        }

        if (!Directory.Exists(source.FullName))
        {
            return Fail($"源目录不存在：{source.FullName}");
        }

        if (string.Equals(source.FullName, target.FullName, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("目标目录和当前目录是同一个");
        }

        if (IsInside(target.FullName, source.FullName) || IsInside(source.FullName, target.FullName))
        {
            return Fail("目标目录不能放在当前目录里面，也不能包含当前目录");
        }

        if (Directory.Exists(target.FullName) && Directory.EnumerateFileSystemEntries(target.FullName).Any())
        {
            return Fail("目标目录不是空的，换一个空目录");
        }

        try
        {
            foreach (var file in EnumeratePayload(source.FullName))
            {
                var relative = Path.GetRelativePath(source.FullName, file.FullName);
                files.Add(new MigrationFile(relative, file.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail($"读取源目录失败：{ex.Message}");
        }

        var total = files.Sum(f => f.Length);
        if (total == 0 && files.Count == 0)
        {
            return new MigrationInspection(true, null, files, 0);
        }

        // 留一倍余量：复制过程中源目录可能还在写（例如日志）。
        if (targetFreeBytes < total * 2)
        {
            return Fail(
                $"目标磁盘剩余空间不足：需要 {FormatBytes(total * 2)}，可用 {FormatBytes(Math.Max(targetFreeBytes, 0))}");
        }

        return new MigrationInspection(true, null, files, total);
    }

    /// <summary>
    /// 真正执行迁移。成功时新目录已就位、源目录写 <c>.migrated.json</c> 但**不删除**。
    /// 需要调用方在成功后写指针并重启。
    /// </summary>
    public static MigrationResult Migrate(
        string sourceRoot,
        string targetRoot,
        Func<string, long> freeBytesAt,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(freeBytesAt);

        var source = TryNormalize(sourceRoot);
        var target = TryNormalize(targetRoot);
        if (source is null || target is null)
        {
            return new MigrationResult(false, "路径不合法", null, 0, 0);
        }

        long freeBytes;
        try
        {
            freeBytes = freeBytesAt(target.FullName);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return new MigrationResult(false, $"无法读取目标磁盘空间：{ex.Message}", null, 0, 0);
        }

        var inspection = Inspect(source.FullName, target.FullName, freeBytes);
        if (!inspection.CanMigrate)
        {
            return new MigrationResult(false, inspection.Error, null, 0, 0);
        }

        var staging = BuildStagingPath(target.FullName);
        try
        {
            log?.Invoke($"迁移开始：{source.FullName} → {target.FullName}（{inspection.Files.Count} 个文件，{FormatBytes(inspection.TotalBytes)}）");
            Directory.CreateDirectory(staging);

            // 逐个文件核对大小：迁移期间日志还在长，不能拿迁移前扫描的总字节数当期望值。
            var copied = new List<MigrationFile>(inspection.Files.Count);
            foreach (var file in inspection.Files)
            {
                var from = Path.Combine(source.FullName, file.RelativePath);
                var to = Path.Combine(staging, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);

                var expected = 0L;
                for (var attempt = 1; ; attempt++)
                {
                    expected = new FileInfo(from).Length;
                    File.Copy(from, to, overwrite: attempt > 1);
                    var actual = new FileInfo(to).Length;
                    if (actual == expected)
                    {
                        break;
                    }

                    if (attempt < 2)
                    {
                        continue;
                    }

                    if (!IsVolatile(file.RelativePath))
                    {
                        return Abort(staging, $"校验失败：{file.RelativePath} 复制后 {actual} 字节 ≠ 复制前 {expected} 字节");
                    }

                    // 日志这类文件在复制过程中还会被追加，复制到的长度就是它当时的长度，照单接收。
                    expected = actual;
                    break;
                }

                File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
                copied.Add(new MigrationFile(file.RelativePath, expected));
            }

            var verifyError = Verify(staging, copied);
            if (verifyError is not null)
            {
                return Abort(staging, verifyError);
            }

            if (Directory.Exists(target.FullName))
            {
                Directory.Delete(target.FullName); // Inspect 已保证是空目录
            }

            var targetParent = Path.GetDirectoryName(target.FullName);
            if (!string.IsNullOrEmpty(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            Directory.Move(staging, target.FullName);
            WriteMigratedMarker(source.FullName, target.FullName);
            var copiedBytes = copied.Sum(f => f.Length);
            log?.Invoke($"迁移完成：{target.FullName}（{copied.Count} 个文件，{FormatBytes(copiedBytes)}）");

            return new MigrationResult(true, null, target.FullName, copied.Count, copiedBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Abort(staging, ex.Message);
        }
    }

    /// <summary>把相对路径写进 <c>.migrated.json</c>，告诉用户旧目录搬去哪了（不删旧数据）。</summary>
    public static void WriteMigratedMarker(string sourceRoot, string targetRoot)
    {
        var marker = new JsonObject
        {
            ["migratedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            ["to"] = targetRoot,
            ["note"] = "数据已迁移，这个目录可以手动删除。",
        };

        var path = Path.Combine(sourceRoot, MigratedMarkerName);
        File.WriteAllText(
            path,
            marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    /// <summary>清理上一次失败留下的暂存目录（启动时调用）。</summary>
    public static int CleanupStaleStaging(string targetRoot)
    {
        var target = TryNormalize(targetRoot);
        if (target is null)
        {
            return 0;
        }

        var parent = Path.GetDirectoryName(target.FullName);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return 0;
        }

        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(parent, ".wantodo-incoming-*"))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception)
            {
                // 删不掉就算了，下次再试。
            }
        }

        return removed;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:0.#} {units[unit]}";
    }

    private static MigrationInspection Fail(string error) => new(false, error, Array.Empty<MigrationFile>(), 0);

    private static MigrationResult Abort(string staging, string error)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception)
        {
            // 暂存目录删不掉不影响源数据。
        }

        return new MigrationResult(false, error, null, 0, 0);
    }

    private static string? Verify(string staging, IReadOnlyList<MigrationFile> copied)
    {
        var staged = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .Select(f => new MigrationFile(Path.GetRelativePath(staging, f), new FileInfo(f).Length))
            .ToList();

        if (staged.Count != copied.Count)
        {
            return $"校验失败：文件数 {staged.Count} ≠ 期望 {copied.Count}";
        }

        var stagedByPath = staged.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var expected in copied)
        {
            if (!stagedByPath.TryGetValue(expected.RelativePath, out var actual))
            {
                return $"校验失败：缺少 {expected.RelativePath}";
            }

            if (actual.Length != expected.Length && !IsVolatile(expected.RelativePath))
            {
                return $"校验失败：{expected.RelativePath} 是 {actual.Length} 字节，复制时是 {expected.Length} 字节";
            }
        }

        // 板数据必须能解析，否则搬过去也打不开。
        var board = Path.Combine(staging, "board", "data.json");
        if (File.Exists(board))
        {
            try
            {
                using var stream = File.OpenRead(board);
                using var _ = JsonDocument.Parse(stream);
            }
            catch (JsonException ex)
            {
                return $"校验失败：board\\data.json 不是合法 JSON（{ex.Message}）";
            }
        }

        return null;
    }

    /// <summary>日志这类文件在迁移期间仍在增长，不能按固定字节数比对。</summary>
    private static bool IsVolatile(string relativePath)
    {
        var normalized = relativePath.Replace('/', '\\');
        return normalized.StartsWith(@"logs\", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<FileInfo> EnumeratePayload(string root)
    {
        // 日志可能正在被写，跳过当前日志的 .tmp；.lock 是「这个目录正被哪个进程用」的凭据，
        // 搬到新目录只会变成一条陈旧锁，新目录启动时会自己写，所以不搬。其余全搬。
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(file.Name, DataPaths.LockFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string BuildStagingPath(string targetRoot)
    {
        var parent = Path.GetDirectoryName(targetRoot);
        if (string.IsNullOrEmpty(parent))
        {
            throw new IOException($"目标路径没有父目录：{targetRoot}");
        }

        return Path.Combine(parent, $".wantodo-incoming-{DateTime.Now:yyyyMMddHHmmssfff}");
    }

    private static DirectoryInfo? TryNormalize(string path)
    {
        try
        {
            return new DirectoryInfo(DataPaths.NormalizeRoot(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsInside(string child, string parent)
    {
        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return child.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
