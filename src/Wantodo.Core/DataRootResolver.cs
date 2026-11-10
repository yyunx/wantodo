using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Wantodo.Core;

/// <summary>可写盘候选。真实实现来自 <see cref="System.IO.DriveInfo"/>。</summary>
public sealed record DriveCandidate(string Name, bool IsSystem, long FreeBytes);

/// <summary>
/// 解析数据根所需的外部世界。真实实现（注册表 / DriveInfo / 探针写文件）在 App 侧，
/// Core 只依赖这个接口，所以选盘逻辑可以单测。
/// </summary>
public interface IDataRootEnvironment
{
    /// <summary>程序自身所在目录。</summary>
    string AppDirectory { get; }

    /// <summary>系统盘，例如 <c>C:</c>。</summary>
    string SystemDrive { get; }

    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>目录能否创建并写入探针文件。实现方负责清理探针。</summary>
    bool IsDirectoryWritable(string path);

    IReadOnlyList<DriveCandidate> FixedDrives();

    /// <summary>注册表里的指针；没有则 null。</summary>
    string? ReadPointer();

    void WritePointer(string root);
}

public enum DataRootSource
{
    /// <summary>程序目录下有 <c>portable.txt</c>。</summary>
    PortableMarker,

    /// <summary>注册表指针指向一个可用的旧根。</summary>
    Pointer,

    /// <summary>程序目录下的 <c>data</c>（程序装在非 C 盘时）。</summary>
    ProgramDirectory,

    /// <summary>枚举到的非系统固定盘。</summary>
    OtherDrive,

    /// <summary>全都不可用，兜底到程序目录（可能在系统盘）。</summary>
    Fallback,
}

public sealed record DataRootResolution(string Root, DataRootSource Source, string? Warning);

/// <summary>
/// 数据根解析：<c>portable.txt</c> → 注册表指针 → 程序目录（非系统盘）→
/// 非系统固定盘按剩余空间降序 → 兜底程序目录并告警。
/// </summary>
public static class DataRootResolver
{
    public const string PortableMarkerName = "portable.txt";
    public const string DataFolderName = "data";
    public const string RootFolderName = "Wantodo";

    public static DataRootResolution Resolve(IDataRootEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        // 1. 便携标记最高优先：插在 U 盘里就该跟着 U 盘走，忽略指针。
        if (env.FileExists(Path.Combine(env.AppDirectory, PortableMarkerName)))
        {
            var portable = Path.Combine(env.AppDirectory, DataFolderName);
            return new DataRootResolution(DataPaths.NormalizeRoot(portable), DataRootSource.PortableMarker, null);
        }

        // 2. 指针还活着吗？
        var pointer = env.ReadPointer();
        if (!string.IsNullOrWhiteSpace(pointer))
        {
            var normalized = SafeNormalize(pointer);
            if (normalized is not null && env.DirectoryExists(normalized) && env.IsDirectoryWritable(normalized))
            {
                return new DataRootResolution(normalized, DataRootSource.Pointer, null);
            }
        }

        // 3. 程序装在非系统盘 → 就地放 data。
        var programData = Path.Combine(env.AppDirectory, DataFolderName);
        if (!DataPaths.IsOnSystemDrive(programData, env.SystemDrive) && env.IsDirectoryWritable(programData))
        {
            var root = DataPaths.NormalizeRoot(programData);
            env.WritePointer(root);
            return new DataRootResolution(root, DataRootSource.ProgramDirectory, null);
        }

        // 4. 非系统固定盘，剩余空间大的优先。只写 <盘>:\Wantodo 这一层。
        var drives = env.FixedDrives()
            .Where(d => !d.IsSystem && !string.IsNullOrWhiteSpace(d.Name))
            .OrderByDescending(d => d.FreeBytes);

        foreach (var drive in drives)
        {
            var candidate = Path.Combine(drive.Name, RootFolderName);
            if (env.IsDirectoryWritable(candidate))
            {
                var root = DataPaths.NormalizeRoot(candidate);
                env.WritePointer(root);
                return new DataRootResolution(root, DataRootSource.OtherDrive, null);
            }
        }

        // 5. 兜底：程序目录。此时数据可能落在系统盘，必须让用户看见。
        var fallback = DataPaths.NormalizeRoot(programData);
        env.WritePointer(fallback);
        return new DataRootResolution(
            fallback,
            DataRootSource.Fallback,
            $"没有找到可写的非系统磁盘，数据将存放在系统盘：{fallback}。可以在设置里改到别的盘。");
    }

    /// <summary>写出首次创建数据根时要放进去的说明文件，方便用户自己备份。</summary>
    public static string BuildReadme(string root) =>
        string.Join(Environment.NewLine, new[]
        {
            "wantodo 数据目录",
            "================",
            string.Empty,
            $"位置：{root}",
            string.Empty,
            "board\\data.json      便签板数据（自动保存，同目录 data.bak 是上一版）",
            "notes\\inbox\\         随心记笔记（纯文本 / Markdown，可直接用别的编辑器打开）",
            "logs\\                运行日志（按天滚动，保留 14 天）",
            "trash\\               删除的笔记（保留 30 天）",
            "config\\              设置",
            string.Empty,
            "备份：直接复制整个文件夹即可。",
            "迁移：程序里「设置 → 数据」可以把整个目录搬到别的盘，旧目录不会自动删除。",
            string.Empty,
        });

    private static string? SafeNormalize(string path)
    {
        try
        {
            return DataPaths.NormalizeRoot(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
