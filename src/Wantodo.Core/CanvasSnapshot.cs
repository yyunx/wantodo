using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wantodo.Core;

/// <summary>
/// 单个表面的快照：<see cref="Capture"/> 把一份 <see cref="CanvasDocument"/> 里属于某个表面的
/// 卡片与图元序列化成字符串，<see cref="Restore"/> 只把这些换回去，另一表面的内容原样不动
/// （0.7.0 P1.2 的撤销作用域，计划书 §4.6）。
/// </summary>
/// <remarks>
/// 为什么用 JSON 字符串而不是对象引用：撤销栈要能压 50 份，字符串是不可变值，
/// 不会被后续编辑原地改掉；而且格式与落盘一致，出问题时可以直接和 data.json 对照。
/// </remarks>
public static class CanvasSnapshot
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>抓一份只含 <paramref name="surface"/> 内容的快照。</summary>
    public static string Capture(CanvasDocument document, string surface)
    {
        ArgumentNullException.ThrowIfNull(document);
        var id = CanvasSurfaces.Normalize(surface);
        var data = new SurfaceData
        {
            Cards = document.Cards.Where(card => CanvasSurfaces.Normalize(card.Surface) == id).ToList(),
            Objects = document.Objects.Where(item => CanvasSurfaces.Normalize(item.Surface) == id).ToList(),
        };
        return JsonSerializer.Serialize(data, Options);
    }

    /// <summary>
    /// 把快照里的内容装回文档：先删掉该表面现有的卡片/图元，再补上快照里的，
    /// 最后整体 <see cref="CanvasDocument.Normalize"/>（去重 id、夹取尺寸、补视口）。
    /// 另一表面的内容不受影响。
    /// </summary>
    public static void Restore(CanvasDocument document, string surface, string json)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        SurfaceData? data;
        try
        {
            data = JsonSerializer.Deserialize<SurfaceData>(json, Options);
        }
        catch (JsonException)
        {
            return;
        }

        if (data is null)
        {
            return;
        }

        var id = CanvasSurfaces.Normalize(surface);
        document.Cards.RemoveAll(card => CanvasSurfaces.Normalize(card.Surface) == id);
        document.Objects.RemoveAll(item => CanvasSurfaces.Normalize(item.Surface) == id);
        document.Cards.AddRange(data.Cards);
        document.Objects.AddRange(data.Objects);
        document.Normalize();
    }

    /// <summary>快照里属于该表面的图元数量（状态行用）。</summary>
    public static int Count(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            var data = JsonSerializer.Deserialize<SurfaceData>(json, Options);
            return data is null ? 0 : data.Cards.Count + data.Objects.Count;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private sealed class SurfaceData
    {
        public List<CanvasItem> Cards { get; set; } = new();

        public List<CanvasObject> Objects { get; set; } = new();
    }
}
