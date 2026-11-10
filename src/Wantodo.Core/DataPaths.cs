using System;
using System.Collections.Generic;
using System.IO;

namespace Wantodo.Core;

/// <summary>
/// 数据根（Data Root）下的路径布局。所有会写盘的东西都必须从这里取路径，
/// 不许在别处再拼 <c>%APPDATA%</c>。设计见 docs/design/data-root.md。
/// </summary>
public sealed class DataPaths
{
    public const string BoardDirName = "board";
    public const string ConfigDirName = "config";
    public const string NotesDirName = "notes";
    public const string LogsDirName = "logs";
    public const string TrashDirName = "trash";
    public const string IncomingDirName = ".incoming";
    public const string LockFileName = ".lock";
    public const string ReadmeFileName = "README.txt";

    private DataPaths(string root)
    {
        Root = NormalizeRoot(root);
    }

    /// <summary>数据根，例如 <c>E:\Wantodo</c>。</summary>
    public string Root { get; }

    public string BoardDir => Path.Combine(Root, BoardDirName);

    public string ConfigDir => Path.Combine(Root, ConfigDirName);

    public string NotesDir => Path.Combine(Root, NotesDirName);

    public string LogsDir => Path.Combine(Root, LogsDirName);

    public string TrashDir => Path.Combine(Root, TrashDirName);

    public string IncomingDir => Path.Combine(Root, IncomingDirName);

    public string LockFile => Path.Combine(Root, LockFileName);

    public string ReadmeFile => Path.Combine(Root, ReadmeFileName);

    /// <summary>便签板数据（原子写：同目录 <c>data.json.tmp</c> → <c>File.Replace</c>）。</summary>
    public string BoardFile => Path.Combine(BoardDir, "data.json");

    /// <summary>应用设置。Phase 7 暂不与板数据拆开，保留给 Phase 8/9 用。</summary>
    public string SettingsFile => Path.Combine(ConfigDir, "settings.json");

    /// <summary>首次创建数据根时必须存在的目录。</summary>
    public IReadOnlyList<string> RequiredDirectories => new[]
    {
        Root,
        BoardDir,
        ConfigDir,
        NotesDir,
        Path.Combine(NotesDir, "inbox"),
        LogsDir,
        TrashDir,
    };

    public static DataPaths For(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("数据根不能为空", nameof(root));
        }

        return new DataPaths(root);
    }

    /// <summary>把根目录规范化：去掉结尾分隔符、展开为绝对路径。不做存在性检查。</summary>
    public static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("数据根不能为空", nameof(root));
        }

        var full = Path.GetFullPath(root.Trim());
        return full.Length > 3 ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    /// <summary>建出所有必需目录。已存在不报错。</summary>
    public void EnsureCreated()
    {
        foreach (var dir in RequiredDirectories)
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>路径是否落在系统盘（默认 <c>C:</c>）。用于「数据不落 C 盘」的判定。</summary>
    public static bool IsOnSystemDrive(string path, string systemDrive)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var normalizedSystem = systemDrive.TrimEnd('\\', '/');
        if (!normalizedSystem.EndsWith(':'))
        {
            normalizedSystem += ":";
        }

        return string.Equals(
            root.TrimEnd('\\', '/'),
            normalizedSystem,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>两个路径是否指向同一目录（忽略大小写与尾部分隔符）。</summary>
    public static bool IsSameDirectory(string left, string right)
        => string.Equals(
            NormalizeRoot(left),
            NormalizeRoot(right),
            StringComparison.OrdinalIgnoreCase);
}
