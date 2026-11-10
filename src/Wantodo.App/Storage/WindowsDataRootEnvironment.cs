using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Wantodo.Core;

namespace Wantodo.App.Storage;

/// <summary>
/// 真实世界的数据根环境：注册表指针、固定盘枚举、可写探针。
/// 选盘逻辑本身在 <see cref="DataRootResolver"/> 里，这里只负责「问系统」。
/// </summary>
internal sealed class WindowsDataRootEnvironment : IDataRootEnvironment
{
    private const string RegistryKeyPath = @"Software\wantodo";
    private const string PointerValueName = "DataRoot";
    private const string SchemaValueName = "SchemaVersion";
    private const int SchemaVersion = 2;

    public string AppDirectory => AppContext.BaseDirectory;

    public string SystemDrive =>
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool IsDirectoryWritable(string path)
    {
        var probe = Path.Combine(path, $".probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(probe, "probe");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception)
            {
                // 探针删不掉不算失败
            }
        }
    }

    public IReadOnlyList<DriveCandidate> FixedDrives()
    {
        var system = SystemDrive.TrimEnd('\\');
        var result = new List<DriveCandidate>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }

            long free;
            try
            {
                free = drive.AvailableFreeSpace;
            }
            catch (Exception)
            {
                free = 0;
            }

            result.Add(new DriveCandidate(drive.Name, string.Equals(drive.Name.TrimEnd('\\'), system, StringComparison.OrdinalIgnoreCase), free));
        }

        return result;
    }

    public string? ReadPointer()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(PointerValueName) is string fromRegistry && !string.IsNullOrWhiteSpace(fromRegistry))
            {
                return fromRegistry;
            }
        }
        catch (Exception)
        {
            // 注册表读不到就走降级路径
        }

        return ReadFallbackPointer();
    }

    public void WritePointer(string root)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(PointerValueName, root, RegistryValueKind.String);
            key.SetValue(SchemaValueName, SchemaVersion, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // 注册表不可写时降级到 %APPDATA%\wantodo\location.json（<200 B）。
            try
            {
                var fallbackDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "wantodo");
                Directory.CreateDirectory(fallbackDir);
                File.WriteAllText(Path.Combine(fallbackDir, "location.json"), $"{{\"dataRoot\":\"{root.Replace("\\", "\\\\")}\"}}");
            }
            catch (Exception)
            {
                // 都写不了就只能每次重新解析，不影响数据本身。
            }
        }
    }

    /// <summary>读指针的降级路径：注册表没有时看 location.json。</summary>
    public string? ReadFallbackPointer()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "wantodo",
                "location.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("dataRoot", out var value) ? value.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
