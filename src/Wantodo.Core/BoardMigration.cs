using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wantodo.Core;

/// <summary>
/// <c>data.json</c> 的 schema 迁移（0.7.0：Version 2 → 3）。
/// 迁移是纯函数，App 层不许自己判断版本；备份与文件改名也在 Core 里做，便于单测。
/// </summary>
public static class BoardMigration
{
    /// <summary>当前 schema 版本：3 = 双表面画布 + themeId/paletteId（见 docs/plan-0.7.0.md §1.4）。</summary>
    public const int CurrentVersion = 3;

    /// <summary>迁移前备份的文件名（只生成一次，存在则跳过）。</summary>
    public const string PreUpgradeBackupName = "data.json.pre-070.bak";

    public static bool NeedsUpgrade(Board board) => board.Version < CurrentVersion;

    /// <summary>把任意版本（含缺失字段）的 <see cref="Board"/> 修成当前 schema。诊断写进 diagnostics。</summary>
    public static void Upgrade(Board board, List<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        var from = board.Version;

        // 画布节点：旧文件没有这个字段 → 空画布（真数据在 canvas.json 里，由 CanvasLegacyImport 并入）
        board.Canvas ??= new CanvasDocument();
        board.Canvas.Normalize();

        // 旧 formId / ThemeEffects 只是历史别名，P0.2 起不再参与渲染；这里保留原值以便回滚。
        if (from < CurrentVersion && !string.IsNullOrWhiteSpace(board.FormId))
        {
            diagnostics?.Add($"[迁移] 旧形式 {board.FormId} → liquidGlass（字段保留只读）");
        }

        board.Version = CurrentVersion;
        if (from < CurrentVersion)
        {
            diagnostics?.Add($"[迁移] 数据版本 {from} → {CurrentVersion}");
        }
    }
}

/// <summary>
/// 把旧 <c>board/canvas.json</c> 并入 <c>data.json.canvas</c>，成功后把旧文件改名保留（不删）。
/// 设计见 docs/plan-0.7.0.md §1.4、§4.4。
/// </summary>
public static class CanvasLegacyImport
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>旧文件内容：只有一个 <c>items</c> 列表。</summary>
    private sealed class LegacyFile
    {
        public int Version { get; set; } = 1;

        [JsonPropertyName("items")]
        public List<CanvasItem> Items { get; set; } = new();
    }

    public sealed record Result(
        bool Imported,
        string? RenamedTo,
        int Cards,
        int Objects,
        string? Skipped,
        string? Error);

    /// <summary>
    /// 导入一次。已存在卡片/图元时以 data.json 为准（记 WARN 不覆盖）；坏文件只记错误，不动任何文件。
    /// </summary>
    public static Result Import(Board board, string legacyPath, List<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!File.Exists(legacyPath))
        {
            return new Result(false, null, 0, 0, "没有旧画布文件", null);
        }

        board.Canvas ??= new CanvasDocument();
        if (board.Canvas.Cards.Count > 0 || board.Canvas.Objects.Count > 0)
        {
            const string skipped = "data.json 已有画布内容，旧 canvas.json 不覆盖";
            diagnostics?.Add($"[迁移] {skipped}（保留 {legacyPath}）");
            return new Result(false, null, 0, 0, skipped, null);
        }

        LegacyFile? legacy;
        try
        {
            var json = File.ReadAllText(legacyPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Result(false, null, 0, 0, null, "canvas.json 为空");
            }

            legacy = JsonSerializer.Deserialize<LegacyFile>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics?.Add($"[迁移] canvas.json 解析失败：{ex.Message}");
            return new Result(false, null, 0, 0, null, ex.Message);
        }

        if (legacy?.Items is null || legacy.Items.Count == 0)
        {
            return Rename(board, legacyPath, 0, 0, diagnostics);
        }

        var cards = 0;
        var objects = 0;
        foreach (var item in legacy.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Kind))
            {
                continue;
            }

            item.Normalize();
            if (string.Equals(item.Kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                var obj = new CanvasObject
                {
                    Id = item.Id,
                    Kind = "image",
                    X = item.X,
                    Y = item.Y,
                    W = Math.Max(CanvasObject.MinWidth, item.W),
                    H = Math.Max(CanvasObject.MinHeight, item.H),
                    ZIndex = item.ZIndex,
                    Payload = new Dictionary<string, string>(item.Payload, StringComparer.Ordinal),
                };
                board.Canvas.Objects.Add(obj.Normalize());
                objects++;
            }
            else
            {
                board.Canvas.Cards.Add(item.Normalize());
                cards++;
            }
        }

        board.Canvas.Normalize();
        return Rename(board, legacyPath, cards, objects, diagnostics);
    }

    private static Result Rename(Board board, string legacyPath, int cards, int objects, List<string>? diagnostics)
    {
        var dir = Path.GetDirectoryName(legacyPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(legacyPath);
        var ext = Path.GetExtension(legacyPath);
        var target = Path.Combine(dir, $"{name}.imported-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");

        try
        {
            File.Move(legacyPath, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics?.Add($"[迁移] 旧画布改名失败：{ex.Message}");
            return new Result(true, null, cards, objects, null, ex.Message);
        }

        diagnostics?.Add($"[迁移] 旧画布已并入 data.json（卡片 {cards}、图元 {objects}），文件改名为 {Path.GetFileName(target)}");
        return new Result(true, target, cards, objects, null, null);
    }
}
