using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Wantodo.Core;

public enum BoardLoadStatus
{
    /// <summary>第一次运行，没有数据文件。</summary>
    FirstRun,

    /// <summary>正常读取。</summary>
    Loaded,

    /// <summary>主文件坏了，成功回退到 data.bak。</summary>
    RecoveredFromBackup,

    /// <summary>主文件和备份都坏了，已隔离坏文件并起空白库。</summary>
    RecreatedAfterCorruption,
}

public sealed class BoardLoadResult
{
    public BoardLoadResult(Board board, BoardLoadStatus status, string? message = null)
    {
        Board = board;
        Status = status;
        Message = message;
    }

    public Board Board { get; }

    public BoardLoadStatus Status { get; }

    /// <summary>诊断信息，写日志用；正常情况下为 null。</summary>
    public string? Message { get; }
}

/// <summary>
/// 单文件 JSON 仓储：<c>%APPDATA%\wantodo\data.json</c>。
/// 写入必须原子（AGENTS.md 硬规则 4），读取必须容忍半截文件与坏备份。
/// </summary>
public sealed class JsonBoardStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 中文直接写成中文，方便用户手改 data.json（默认会把非 ASCII 转成 \uXXXX）
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public JsonBoardStore(string? dataPath = null)
    {
        DataPath = dataPath ?? DefaultDataPath();
        DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(DataPath))
                        ?? throw new ArgumentException("dataPath 必须有目录部分", nameof(dataPath));
        TemporaryPath = DataPath + ".tmp";
        BackupPath = Path.Combine(DirectoryPath, "data.bak");
    }

    public string DataPath { get; }

    public string DirectoryPath { get; }

    public string TemporaryPath { get; }

    public string BackupPath { get; }

    /// <summary>诊断输出（首次保存降级、坏文件隔离等），测试与日志都读它。</summary>
    public List<string> Diagnostics { get; } = new();

    public static string DefaultDataPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "wantodo",
        "data.json");

    public BoardLoadResult Load()
    {
        if (!File.Exists(DataPath) && !File.Exists(BackupPath))
        {
            return new BoardLoadResult(Board.CreateDefault(), BoardLoadStatus.FirstRun);
        }

        if (TryRead(DataPath, out var board, out var primaryError))
        {
            return new BoardLoadResult(board!, BoardLoadStatus.Loaded);
        }

        if (TryRead(BackupPath, out var backup, out _))
        {
            var quarantined = Quarantine(DataPath);
            Diagnostics.Add(
                $"主文件不可用（{primaryError}），已回退 data.bak；" +
                (quarantined is null ? "主文件隔离失败" : $"主文件已隔离为 {quarantined}"));
            return new BoardLoadResult(backup!, BoardLoadStatus.RecoveredFromBackup, primaryError);
        }

        var a = Quarantine(DataPath);
        var b = Quarantine(BackupPath);
        Diagnostics.Add($"主文件与备份都不可用（{primaryError}），已隔离：{a ?? "无"} / {b ?? "无"}");
        return new BoardLoadResult(Board.CreateDefault(), BoardLoadStatus.RecreatedAfterCorruption, primaryError);
    }

    public void Save(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        Normalize(board);

        Directory.CreateDirectory(DirectoryPath);

        var json = JsonSerializer.Serialize(board, SerializerOptions);
        WriteTemporary(json);

        if (!File.Exists(DataPath))
        {
            // 首次保存：没有可替换的目标，File.Replace 会抛 FileNotFoundException
            File.Move(TemporaryPath, DataPath);
            return;
        }

        try
        {
            File.Replace(TemporaryPath, DataPath, BackupPath, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 有些文件系统 / 杀软占用会让 Replace 失败，退到"先备份再覆盖"，宁可慢也不能丢数据
            Diagnostics.Add($"File.Replace 失败（{ex.GetType().Name}: {ex.Message}），降级为 Copy+Move");
            File.Copy(DataPath, BackupPath, overwrite: true);
            File.Move(TemporaryPath, DataPath, overwrite: true);
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

    private static bool TryRead(string path, out Board? board, out string? error)
    {
        board = null;
        error = null;

        if (!File.Exists(path))
        {
            error = "文件不存在";
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "文件为空";
                return false;
            }

            var parsed = JsonSerializer.Deserialize<Board>(json, SerializerOptions);
            if (parsed is null)
            {
                error = "反序列化结果为 null";
                return false;
            }

            Normalize(parsed);
            board = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>把字段缺失 / 手写出错的数据补成可用状态。不改内容，只补默认值。</summary>
    internal static void Normalize(Board board)
    {
        board.Placement ??= new WindowPlacement();
        board.Columns ??= new List<NoteColumn>();

        if (board.Placement.Width <= 0)
        {
            board.Placement.Width = 560;
        }

        if (board.Placement.Height <= 0)
        {
            board.Placement.Height = 320;
        }

        if (board.Tone < 0)
        {
            board.Tone = 0;
        }
        else if (board.Tone > 1)
        {
            board.Tone = 1;
        }

        foreach (var column in board.Columns)
        {
            column.Id = string.IsNullOrWhiteSpace(column.Id) ? Guid.NewGuid().ToString("n") : column.Id;
            column.Title = string.IsNullOrWhiteSpace(column.Title) ? "未命名栏" : column.Title;
            column.Notes ??= new List<Note>();

            foreach (var note in column.Notes)
            {
                note.Id = string.IsNullOrWhiteSpace(note.Id) ? Guid.NewGuid().ToString("n") : note.Id;
                note.Text ??= string.Empty;
            }
        }
    }

    private static string? Quarantine(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var target = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"data.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

        try
        {
            File.Move(path, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
