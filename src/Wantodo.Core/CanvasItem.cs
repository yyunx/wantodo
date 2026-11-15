using System.Text.Json.Serialization;

namespace Wantodo.Core;

/// <summary>画布上的一张卡片。坐标与尺寸单位是 DIP，<see cref="Kind"/> 决定 payload 怎么解释。</summary>
public sealed class CanvasItem
{
    public const double MinWidth = 140;
    public const double MaxWidth = 640;
    public const double MinHeight = 90;
    public const double MaxHeight = 640;

    public string Id { get; set; } = "";

    /// <summary>note / todo / timer / link / image。</summary>
    public string Kind { get; set; } = "note";

    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; } = 220;

    public double H { get; set; } = 160;

    public int ZIndex { get; set; } = 1;

    /// <summary>0.7.0 只有 paper 一种。</summary>
    public string Style { get; set; } = "paper";

    /// <summary>按 kind 解释：note→noteId、todo→columnId、timer→seconds、link→url、image→path。</summary>
    public Dictionary<string, string> Payload { get; set; } = new();

    public CanvasItem Normalize()
    {
        W = double.IsNaN(W) || double.IsInfinity(W) ? 220 : Math.Clamp(W, MinWidth, MaxWidth);
        H = double.IsNaN(H) || double.IsInfinity(H) ? 160 : Math.Clamp(H, MinHeight, MaxHeight);
        X = double.IsNaN(X) || double.IsInfinity(X) ? 0 : Math.Max(0, X);
        Y = double.IsNaN(Y) || double.IsInfinity(Y) ? 0 : Math.Max(0, Y);
        ZIndex = Math.Clamp(ZIndex, 1, 999);
        Kind = string.IsNullOrWhiteSpace(Kind) ? "note" : Kind;
        Style = string.IsNullOrWhiteSpace(Style) ? "paper" : Style;
        Payload ??= new Dictionary<string, string>();
        return this;
    }

    public string? Get(string key) => Payload.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// 画布文档：两棵集合（结构化表面的 <see cref="Cards"/>、自由表面的 <see cref="Objects"/>）+ 每表面视口。
/// 0.7.0 起住在 <c>data.json</c> 的 <c>canvas</c> 节点里，不再单独一个文件（见 docs/plan-0.7.0.md §4.4）。
/// </summary>
public sealed class CanvasDocument
{
    /// <summary>schema 版本：1 = 旧 <c>canvas.json</c>（只有 items），2 = 双表面。</summary>
    public int Version { get; set; } = 2;

    /// <summary>当前活动表面；切换只改它，不重建控件。</summary>
    public string ActiveSurfaceId { get; set; } = CanvasSurfaces.Structured;

    /// <summary>结构化表面的卡片（note/todo/timer/link）。JSON 字段名 <c>cards</c>。</summary>
    [JsonPropertyName("cards")]
    public List<CanvasItem> Cards { get; set; } = new();

    /// <summary>自由表面的图元（image/stroke/shape/text）。</summary>
    public List<CanvasObject> Objects { get; set; } = new();

    /// <summary>每个表面自己的缩放与平移，key 是表面 id。</summary>
    public Dictionary<string, CanvasViewport> Viewports { get; set; } = new();

    /// <summary>0.7.0 之前的调用方（CanvasBoard）按 <c>Items</c> 读写卡片；P0.3 改完再删别名。</summary>
    [JsonIgnore]
    public List<CanvasItem> Items => Cards;

    /// <summary>夹取非法值、去掉空 id 与重复 id、补视口；返回被删掉的个数。</summary>
    public int Normalize()
    {
        Cards ??= new List<CanvasItem>();
        Objects ??= new List<CanvasObject>();
        Viewports ??= new Dictionary<string, CanvasViewport>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = Cards.RemoveAll(item => item is null || string.IsNullOrWhiteSpace(item.Id));
        removed += Objects.RemoveAll(item => item is null || string.IsNullOrWhiteSpace(item.Id));

        foreach (var card in Cards)
        {
            card.Normalize();
            if (!seen.Add(card.Id))
            {
                card.Id = NextId(seen);
                seen.Add(card.Id);
            }
        }

        foreach (var obj in Objects)
        {
            obj.Normalize();
            if (!seen.Add(obj.Id))
            {
                obj.Id = NextObjectId(seen);
                seen.Add(obj.Id);
            }
        }

        ActiveSurfaceId = CanvasSurfaces.Normalize(ActiveSurfaceId);

        var viewports = new Dictionary<string, CanvasViewport>(StringComparer.Ordinal);
        foreach (var surface in new[] { CanvasSurfaces.Structured, CanvasSurfaces.Freeform })
        {
            viewports[surface] = Viewports.TryGetValue(surface, out var vp) && vp is not null
                ? vp.Normalize()
                : new CanvasViewport();
        }

        Viewports = viewports;

        var stale = Viewports.Keys
            .Where(k => !CanvasSurfaces.IsKnown(k))
            .ToList();
        foreach (var key in stale)
        {
            Viewports.Remove(key);
        }

        Version = Math.Max(Version, 2);
        return removed;
    }

    /// <summary>生成文件内唯一的 <c>cN</c> 卡片 id。</summary>
    public string NextId(IEnumerable<string>? existing = null)
    {
        var used = new HashSet<string>(
            existing ?? Cards.Select(i => i.Id).Concat(Objects.Select(o => o.Id)),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < 100_000; i++)
        {
            var candidate = "c" + i;
            if (used.Add(candidate))
            {
                return candidate;
            }
        }

        return "c" + Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>生成文件内唯一的 <c>oN</c> 图元 id。</summary>
    public string NextObjectId(IEnumerable<string>? existing = null)
    {
        var used = new HashSet<string>(
            existing ?? Cards.Select(i => i.Id).Concat(Objects.Select(o => o.Id)),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < 100_000; i++)
        {
            var candidate = "o" + i;
            if (used.Add(candidate))
            {
                return candidate;
            }
        }

        return "o" + Guid.NewGuid().ToString("N")[..8];
    }

    public CanvasItem Add(string kind, double x, double y)
    {
        var item = new CanvasItem
        {
            Id = NextId(),
            Kind = kind,
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            ZIndex = Cards.Count == 0 ? 1 : Math.Min(999, Cards.Max(i => i.ZIndex) + 1),
        };

        Cards.Add(item);
        return item;
    }

    public CanvasObject AddObject(string kind, double x, double y, double w, double h)
    {
        var obj = new CanvasObject
        {
            Id = NextObjectId(),
            Kind = kind,
            X = x,
            Y = y,
            W = w,
            H = h,
            ZIndex = Objects.Count == 0 ? 1 : Math.Min(9999, Objects.Max(o => o.ZIndex) + 1),
        };

        Objects.Add(obj);
        return obj;
    }

    public CanvasViewport ViewportOf(string surface)
    {
        var id = CanvasSurfaces.Normalize(surface);
        if (!Viewports.TryGetValue(id, out var vp) || vp is null)
        {
            vp = new CanvasViewport();
            Viewports[id] = vp;
        }

        return vp.Normalize();
    }
}
