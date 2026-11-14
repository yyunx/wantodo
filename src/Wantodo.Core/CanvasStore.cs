using System.Text;
using System.Text.Json;

namespace Wantodo.Core;

/// <summary>
/// <c>board/canvas.json</c> 的读写。落盘沿用 JsonBoardStore 的原子写：
/// <c>canvas.json.tmp</c> → <c>File.Replace</c>（首次 <c>File.Move</c>），失败降级 Copy+Move。
/// </summary>
public sealed class CanvasStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public CanvasStore(string? path = null)
    {
        FilePath = path ?? DefaultPath();
        DirectoryPath = Path.GetDirectoryName(FilePath) ?? ".";
        TemporaryPath = FilePath + ".tmp";
        BackupPath = FilePath + ".bak";
    }

    public string FilePath { get; }

    public string DirectoryPath { get; }

    public string TemporaryPath { get; }

    public string BackupPath { get; }

    public List<string> Diagnostics { get; } = new();

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wantodo", "board", "canvas.json");

    /// <summary>读画布；文件缺失视为空画布，坏文件回退 .bak，再坏就隔离后给空画布。</summary>
    public CanvasDocument Load()
    {
        if (TryRead(FilePath, out var document))
        {
            document!.Normalize();
            return document;
        }

        if (File.Exists(BackupPath) && TryRead(BackupPath, out var backup))
        {
            Diagnostics.Add("主文件不可用，已从 canvas.json.bak 恢复");
            backup!.Normalize();
            return backup;
        }

        if (File.Exists(FilePath) || File.Exists(BackupPath))
        {
            Diagnostics.Add($"画布文件不可用，已隔离为 {Quarantine(FilePath) ?? "无"} / {Quarantine(BackupPath) ?? "无"}");
        }

        return new CanvasDocument();
    }

    public void Save(CanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Normalize();

        Directory.CreateDirectory(DirectoryPath);
        WriteTemporary(JsonSerializer.Serialize(document, SerializerOptions));

        if (!File.Exists(FilePath))
        {
            File.Move(TemporaryPath, FilePath);
            return;
        }

        try
        {
            File.Replace(TemporaryPath, FilePath, BackupPath, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Diagnostics.Add($"File.Replace 失败（{ex.GetType().Name}: {ex.Message}），降级为 Copy+Move");
            File.Copy(FilePath, BackupPath, overwrite: true);
            File.Move(TemporaryPath, FilePath, overwrite: true);
        }
    }

    private void WriteTemporary(string json)
    {
        using var stream = new FileStream(
            TemporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static bool TryRead(string path, out CanvasDocument? document)
    {
        document = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            document = JsonSerializer.Deserialize<CanvasDocument>(json, SerializerOptions);
            return document is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Quarantine(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var target = $"{path}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            File.Move(path, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
