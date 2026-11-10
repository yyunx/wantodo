using System;
using System.IO;
using Wantodo.Core;

namespace Wantodo.App.Storage;

/// <summary>
/// 数据根的启动流程：解析 → 建目录 → 拿锁 → 导入 v0.4 数据 → 清理过期文件。
/// 只在 App.OnStartup 里调用一次，之后全程序用 <see cref="Paths"/>。
/// </summary>
internal static class DataRootService
{
    private static WindowsDataRootEnvironment? _environment;
    private static RootLock? _lock;

    public static DataPaths Paths { get; private set; } = null!;

    public static DataRootResolution Resolution { get; private set; } = null!;

    public static WindowsDataRootEnvironment Host
        => _environment ?? throw new InvalidOperationException("DataRootService 还没初始化");

    /// <summary>v0.4 的旧目录（%APPDATA%\wantodo），只用来做一次性导入与「清理旧数据」。</summary>
    public static string LegacyDirectory => Path.Combine(
        System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wantodo");

    public static string LegacyBoardFile => Path.Combine(LegacyDirectory, "data.json");

    public static void Initialize()
    {
        _environment = new WindowsDataRootEnvironment();
        Resolution = DataRootResolver.Resolve(_environment);
        Paths = DataPaths.For(Resolution.Root);
        Paths.EnsureCreated();

        if (!File.Exists(Paths.ReadmeFile))
        {
            try
            {
                File.WriteAllText(
                    Paths.ReadmeFile,
                    DataRootResolver.BuildReadme(Paths.Root),
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 说明文件写不了不影响使用
            }
        }

        // 上一次迁移失败留下的暂存目录先清掉。
        DataMigrator.CleanupStaleStaging(Paths.Root);

        _lock = RootLock.TryAcquire(Paths.LockFile, out var lockMessage);
        if (_lock is null)
        {
            // 不阻塞启动：只是记下来，真正的单实例由 Mutex 保证。
            LastLockMessage = lockMessage;
        }
    }

    public static string? LastLockMessage { get; private set; }

    /// <summary>把 v0.4 的 <c>%APPDATA%\wantodo\data.json</c> 导入到新根的 <c>board\data.json</c>。</summary>
    public static string? ImportLegacyBoard()
    {
        try
        {
            if (File.Exists(Paths.BoardFile) || !File.Exists(LegacyBoardFile))
            {
                return null;
            }

            Directory.CreateDirectory(Paths.BoardDir);
            File.Copy(LegacyBoardFile, Paths.BoardFile, overwrite: false);

            var bak = LegacyBoardFile + ".bak";
            if (File.Exists(bak))
            {
                File.Copy(bak, Paths.BoardFile + ".bak", overwrite: false);
            }

            // 旧文件改名留档，不删（用户可能还想翻）。
            var archived = Path.Combine(LegacyDirectory, "data.migrated.json");
            File.Move(LegacyBoardFile, archived, overwrite: true);

            return $"已把旧数据导入到 {Paths.BoardFile}（旧文件留档为 {archived}）";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"导入旧数据失败：{ex.Message}";
        }
    }

    /// <summary>清理日志与回收站里过期的文件，返回 (日志, 回收站)。</summary>
    public static (int Logs, int Trash) Prune()
    {
        try
        {
            return DataRetention.PruneAll(Paths, DateTimeOffset.Now);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    public static long FreeBytesAt(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public static void Shutdown() => _lock?.Dispose();
}
