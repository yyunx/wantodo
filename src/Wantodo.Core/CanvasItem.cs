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

/// <summary>board/canvas.json 的根对象；只在 <c>PanelMode=Canvas</c> 时读取。</summary>
public sealed class CanvasDocument
{
    public int Version { get; set; } = 1;

    public List<CanvasItem> Items { get; set; } = new();

    /// <summary>夹取非法值、去掉空 id 与重复 id；返回被修正的项数。</summary>
    public int Normalize()
    {
        Items ??= new List<CanvasItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = Items.RemoveAll(item => item is null || string.IsNullOrWhiteSpace(item.Id));

        foreach (var item in Items)
        {
            item.Normalize();
            if (!seen.Add(item.Id))
            {
                item.Id = NextId(seen);
                seen.Add(item.Id);
            }
        }

        return removed;
    }

    /// <summary>生成文件内唯一的 <c>cN</c> id。</summary>
    public string NextId(IEnumerable<string>? existing = null)
    {
        var used = new HashSet<string>(
            existing ?? Items.Select(i => i.Id),
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

    public CanvasItem Add(string kind, double x, double y)
    {
        var item = new CanvasItem
        {
            Id = NextId(),
            Kind = kind,
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            ZIndex = Items.Count == 0 ? 1 : Math.Min(999, Items.Max(i => i.ZIndex) + 1),
        };

        Items.Add(item);
        return item;
    }
}
