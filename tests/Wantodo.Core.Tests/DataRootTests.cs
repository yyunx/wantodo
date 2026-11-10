using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

public sealed class DataRootResolverTests
{
    [Fact]
    public void PortableMarker_Wins_Over_Pointer_And_Drives()
    {
        var env = new FakeEnv(@"E:\Apps\Wantodo")
        {
            SystemDrive = "C:",
            Pointer = @"D:\Wantodo",
        };
        env.Portable = true;
        env.Drives.Add(new DriveCandidate(@"D:\", true, 500L * 1024 * 1024 * 1024));

        var result = DataRootResolver.Resolve(env);

        Assert.Equal(DataRootSource.PortableMarker, result.Source);
        Assert.Equal(@"E:\Apps\Wantodo\data", result.Root);
        Assert.Null(result.Warning);
        Assert.Null(env.WrittenPointer); // 便携模式不该写指针
    }

    [Fact]
    public void LivePointer_Is_Used_Directly()
    {
        var env = new FakeEnv(@"C:\Program Files\Wantodo")
        {
            Pointer = @"D:\Wantodo",
        };
        env.Writable.Add(DataPaths.NormalizeRoot(@"D:\Wantodo"));

        var result = DataRootResolver.Resolve(env);

        Assert.Equal(DataRootSource.Pointer, result.Source);
        Assert.Equal(@"D:\Wantodo", result.Root);
    }

    [Fact]
    public void DeadPointer_Falls_Back_To_ProgramDirectory_When_Not_On_System_Drive()
    {
        var env = new FakeEnv(@"E:\Apps\Wantodo")
        {
            SystemDrive = "C:",
            Pointer = @"F:\Unplugged\Wantodo", // 目录不存在
        };
        env.Writable.Add(@"E:\Apps\Wantodo\data");

        var result = DataRootResolver.Resolve(env);

        Assert.Equal(DataRootSource.ProgramDirectory, result.Source);
        Assert.Equal(@"E:\Apps\Wantodo\data", result.Root);
        Assert.Equal(@"E:\Apps\Wantodo\data", env.WrittenPointer);
    }

    [Fact]
    public void Program_Directory_On_System_Drive_Picks_Largest_Free_NonSystem_Drive()
    {
        var env = new FakeEnv(@"C:\Program Files\Wantodo");
        env.Drives.Add(new DriveCandidate(@"E:\", false, 10L * 1024 * 1024 * 1024));
        env.Drives.Add(new DriveCandidate(@"D:\", false, 200L * 1024 * 1024 * 1024));
        env.Drives.Add(new DriveCandidate(@"C:\", true, 900L * 1024 * 1024 * 1024));
        env.Writable.Add(@"D:\Wantodo");

        var result = DataRootResolver.Resolve(env);

        Assert.Equal(DataRootSource.OtherDrive, result.Source);
        Assert.Equal(@"D:\Wantodo", result.Root);
        Assert.Equal(@"D:\Wantodo", env.WrittenPointer);
    }

    [Fact]
    public void Skips_Drives_That_Are_Not_Writable()
    {
        var env = new FakeEnv(@"C:\Program Files\Wantodo");
        env.Drives.Add(new DriveCandidate(@"D:\", false, 900L * 1024 * 1024 * 1024));
        env.Drives.Add(new DriveCandidate(@"E:\", false, 10L * 1024 * 1024 * 1024));
        env.Writable.Add(@"E:\Wantodo"); // D 盘不可写

        var result = DataRootResolver.Resolve(env);

        Assert.Equal(@"E:\Wantodo", result.Root);
    }

    [Fact]
    public void All_Failures_Fall_Back_With_Warning()
    {
        var env = new FakeEnv(@"C:\Program Files\Wantodo");
        env.Drives.Add(new DriveCandidate(@"D:\", false, 900L * 1024 * 1024 * 1024));

        var result = DataRootResolver.Resolve(env);

        Assert.Equal(DataRootSource.Fallback, result.Source);
        Assert.Equal(@"C:\Program Files\Wantodo\data", result.Root);
        Assert.NotNull(result.Warning);
        Assert.Contains("系统盘", result.Warning);
    }

    private sealed class FakeEnv : IDataRootEnvironment
    {
        public FakeEnv(string appDirectory) => AppDirectory = appDirectory;

        public string AppDirectory { get; }

        public string SystemDrive { get; set; } = "C:";

        public bool Portable { get; set; }

        public string? Pointer { get; set; }

        public string? WrittenPointer { get; private set; }

        public List<DriveCandidate> Drives { get; } = new();

        public HashSet<string> Writable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => Pointer is not null && PathsEqual(path, Pointer) && Writable.Contains(DataPaths.NormalizeRoot(path));

        public bool FileExists(string path)
            => Portable && string.Equals(
                path,
                Path.Combine(AppDirectory, DataRootResolver.PortableMarkerName),
                StringComparison.OrdinalIgnoreCase);

        public bool IsDirectoryWritable(string path) => Writable.Contains(DataPaths.NormalizeRoot(path));

        public IReadOnlyList<DriveCandidate> FixedDrives() => Drives;

        public string? ReadPointer() => Pointer;

        public void WritePointer(string root) => WrittenPointer = root;

        private static bool PathsEqual(string left, string right)
            => string.Equals(DataPaths.NormalizeRoot(left), DataPaths.NormalizeRoot(right), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DataPathsTests
{
    [Theory]
    [InlineData(@"C:\Wantodo", "C:", true)]
    [InlineData(@"c:\Wantodo", "C:", true)]
    [InlineData(@"D:\Wantodo", "C:", false)]
    [InlineData(@"C:\Program Files\Wantodo\data", "C:", true)]
    public void IsOnSystemDrive_Works(string path, string systemDrive, bool expected)
        => Assert.Equal(expected, DataPaths.IsOnSystemDrive(path, systemDrive));

    [Fact]
    public void NormalizeRoot_Trims_Trailing_Separator()
    {
        Assert.Equal(@"E:\Wantodo", DataPaths.NormalizeRoot(@"E:\Wantodo\"));
        Assert.Equal(@"E:\Wantodo", DataPaths.NormalizeRoot(@"  E:\Wantodo  "));
    }

    [Fact]
    public void RequiredDirectories_Include_Board_Notes_Logs_Trash()
    {
        var paths = DataPaths.For(@"E:\Wantodo");

        Assert.Contains(paths.BoardDir, paths.RequiredDirectories);
        Assert.Contains(paths.LogsDir, paths.RequiredDirectories);
        Assert.Contains(paths.TrashDir, paths.RequiredDirectories);
        Assert.Contains(Path.Combine(paths.NotesDir, "inbox"), paths.RequiredDirectories);
        Assert.Equal(@"E:\Wantodo\board\data.json", paths.BoardFile);
    }

    [Fact]
    public void IsSameDirectory_Ignores_Case_And_Trailing_Slash()
    {
        Assert.True(DataPaths.IsSameDirectory(@"E:\Wantodo\", @"e:\wantodo"));
        Assert.False(DataPaths.IsSameDirectory(@"E:\Wantodo", @"E:\Wantodo2"));
    }
}

public sealed class DataMigratorTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wantodo-migrate", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temp))
            {
                Directory.Delete(_temp, recursive: true);
            }
        }
        catch (Exception)
        {
            // 测试清理失败不影响结论
        }
    }

    [Fact]
    public void Migrate_Copies_Everything_And_Writes_Marker()
    {
        var source = NewRoot("old");
        WriteFile(source, @"board\data.json", "{\"version\":1}");
        WriteFile(source, @"notes\inbox\20261110-2214-a.txt", "hello");
        var target = Path.Combine(_temp, "new");

        var result = DataMigrator.Migrate(source, target, _ => long.MaxValue);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(target, "board", "data.json")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "notes", "inbox", "20261110-2214-a.txt")));
        Assert.True(File.Exists(Path.Combine(source, ".migrated.json")));
        Assert.True(Directory.EnumerateDirectories(_temp).All(d => !Path.GetFileName(d).StartsWith(".wantodo-incoming-")));
    }

    [Fact]
    public void Migrate_Rejects_NonEmpty_Target()
    {
        var source = NewRoot("old");
        WriteFile(source, "board/data.json", "{}");
        var target = Path.Combine(_temp, "busy");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "already.txt"), "x");

        var inspection = DataMigrator.Inspect(source, target, long.MaxValue);

        Assert.False(inspection.CanMigrate);
        Assert.Contains("不是空的", inspection.Error);
    }

    [Fact]
    public void Migrate_Rejects_Insufficient_Space()
    {
        var source = NewRoot("old");
        WriteFile(source, "board/data.json", new string('x', 4096));

        var inspection = DataMigrator.Inspect(source, Path.Combine(_temp, "small"), 1024);

        Assert.False(inspection.CanMigrate);
        Assert.Contains("空间不足", inspection.Error);
    }

    [Fact]
    public void Migrate_Rejects_Nested_Target()
    {
        var source = NewRoot("old");
        WriteFile(source, "board/data.json", "{}");

        var inspection = DataMigrator.Inspect(source, Path.Combine(source, "inner"), long.MaxValue);

        Assert.False(inspection.CanMigrate);
        Assert.Contains("里面", inspection.Error);
    }

    [Fact]
    public void Migrate_Rejects_Broken_Json_And_Leaves_Source_Intact()
    {
        var source = NewRoot("old");
        WriteFile(source, "board/data.json", "{ this is not json");
        var target = Path.Combine(_temp, "new");

        var result = DataMigrator.Migrate(source, target, _ => long.MaxValue);

        Assert.False(result.Success);
        Assert.Contains("合法 JSON", result.Error);
        Assert.True(File.Exists(Path.Combine(source, "board", "data.json")));
        Assert.False(Directory.Exists(target));
        Assert.True(Directory.EnumerateDirectories(_temp).All(d => !Path.GetFileName(d).StartsWith(".wantodo-incoming-")));
    }

    [Fact]
    public void Migrate_Skips_Lock_File_But_Copies_Logs()
    {
        var source = NewRoot("old");
        WriteFile(source, @"board\data.json", "{\"version\":1}");
        WriteFile(source, @"logs\app-20261110.log", "line1" + Environment.NewLine);
        WriteFile(source, ".lock", "123\t2026-11-10T00:00:00+08:00\tPC\tuser");
        var target = Path.Combine(_temp, "new");

        var result = DataMigrator.Migrate(source, target, _ => long.MaxValue);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.FileCount);
        Assert.False(File.Exists(Path.Combine(target, ".lock")));
        Assert.True(File.Exists(Path.Combine(target, "logs", "app-20261110.log")));
    }

    [Fact]
    public void Migrate_Rejects_Same_Directory()
    {
        var source = NewRoot("old");

        var inspection = DataMigrator.Inspect(source, source, long.MaxValue);

        Assert.False(inspection.CanMigrate);
        Assert.Contains("同一个", inspection.Error);
    }

    private string NewRoot(string name)
    {
        var root = Path.Combine(_temp, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteFile(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}

public sealed class DataRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wantodo-retention", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (Exception)
        {
            // ignore
        }
    }

    [Fact]
    public void PruneFiles_Removes_Only_Old_Files()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "app-20260101.log");
        var fresh = Path.Combine(_dir, "app-20261110.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(fresh, "fresh");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-20));

        var removed = DataRetention.PruneFiles(_dir, "app-*.log", TimeSpan.FromDays(14), DateTimeOffset.UtcNow);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void PruneAll_Keeps_Todays_Log_And_Old_Trash_Removed()
    {
        var paths = DataPaths.For(Path.Combine(_dir, "root"));
        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(paths.LogsDir, DataRetention.LogFileName(DateTimeOffset.Now)), "today");
        var oldTrash = Path.Combine(paths.TrashDir, "20260101-x.txt");
        File.WriteAllText(oldTrash, "y");
        File.SetLastWriteTimeUtc(oldTrash, DateTime.UtcNow.AddDays(-40));

        var (logs, trash) = DataRetention.PruneAll(paths, DateTimeOffset.Now);

        Assert.Equal(0, logs);
        Assert.Equal(1, trash);
        Assert.Single(Directory.EnumerateFiles(paths.LogsDir));
    }
}

public sealed class RootLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wantodo-lock", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (Exception)
        {
            // ignore
        }
    }

    [Fact]
    public void TryAcquire_Succeeds_And_Writes_Pid()
    {
        var path = Path.Combine(_dir, ".lock");

        using var root = RootLock.TryAcquire(path, out var message);

        Assert.NotNull(root);
        Assert.Null(message);
        Assert.Contains(Environment.ProcessId.ToString(), File.ReadAllText(path));
    }

    [Fact]
    public void TryAcquire_Fails_When_Same_Process_Holds_It()
    {
        var path = Path.Combine(_dir, ".lock");
        using var first = RootLock.TryAcquire(path, out _);

        var second = RootLock.TryAcquire(path, out var message);

        Assert.Null(second);
        Assert.Contains("占用", message);
    }

    [Fact]
    public void TryAcquire_Takes_Over_Stale_Lock()
    {
        var path = Path.Combine(_dir, ".lock");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, $"999999\t{DateTimeOffset.Now:O}\tX\tY");

        using var root = RootLock.TryAcquire(path, out var message);

        Assert.NotNull(root);
        Assert.Null(message);
    }
}
