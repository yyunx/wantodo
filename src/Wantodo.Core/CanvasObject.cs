namespace Wantodo.Core;

/// <summary>两个画布表面的 id（0.7.0）。写进 JSON 的判别字段，本期只有两个值。</summary>
public static class CanvasSurfaces
{
    /// <summary>结构化表面：承载 note/todo/timer/link 卡片。</summary>
    public const string Structured = "structured";

    /// <summary>自由表面：承载图片/笔迹/形状/文本图元。</summary>
    public const string Freeform = "freeform";

    /// <summary>未知值一律回落到结构化表面（旧数据没有这个字段）。</summary>
    public static string Normalize(string? id) =>
        string.Equals(id, Freeform, StringComparison.OrdinalIgnoreCase) ? Freeform : Structured;

    public static bool IsKnown(string? id) =>
        string.Equals(id, Structured, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, Freeform, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 自由表面上的一个图元。只有几何与样式，不参与便签仓库；<see cref="Kind"/> 决定 payload 怎么解释。
/// 0.7.0 的 kind：<c>image</c> / <c>stroke</c> / <c>shape</c> / <c>text</c>。
/// </summary>
public sealed class CanvasObject
{
    public const double MinWidth = 4;
    public const double MaxWidth = 20000;
    public const double MinHeight = 4;
    public const double MaxHeight = 20000;

    public string Id { get; set; } = "";

    /// <summary>表面判别字段，本期恒为 <c>freeform</c>。</summary>
    public string Surface { get; set; } = CanvasSurfaces.Freeform;

    /// <summary>image / stroke / shape / text。</summary>
    public string Kind { get; set; } = "image";

    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; } = 240;

    public double H { get; set; } = 160;

    public int ZIndex { get; set; } = 1;

    /// <summary>按 kind 解释：image→path/naturalW/naturalH；stroke→path/color/strokeWidth；
    /// shape→shape/x1/y1/x2/y2/color/strokeWidth；text→text/color/fontSize。</summary>
    public Dictionary<string, string> Payload { get; set; } = new();

    public CanvasObject Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? "o" + Guid.NewGuid().ToString("n")[..8] : Id;
        Surface = CanvasSurfaces.Freeform;
        Kind = string.IsNullOrWhiteSpace(Kind) ? "image" : Kind;
        W = double.IsNaN(W) || double.IsInfinity(W) ? 240 : Math.Clamp(W, MinWidth, MaxWidth);
        H = double.IsNaN(H) || double.IsInfinity(H) ? 160 : Math.Clamp(H, MinHeight, MaxHeight);
        X = double.IsNaN(X) || double.IsInfinity(X) ? 0 : X;
        Y = double.IsNaN(Y) || double.IsInfinity(Y) ? 0 : Y;
        ZIndex = Math.Clamp(ZIndex, 1, 9999);
        Payload ??= new Dictionary<string, string>();
        return this;
    }

    public string? Get(string key) => Payload.TryGetValue(key, out var value) ? value : null;

    public double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value)
            ? value
            : fallback;
}

/// <summary>每个表面自己的视口（缩放 + 平移），随画布文档持久化。</summary>
public sealed class CanvasViewport
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.0;

    public double Zoom { get; set; } = 1.0;

    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    public CanvasViewport Normalize()
    {
        Zoom = double.IsNaN(Zoom) || double.IsInfinity(Zoom) ? 1.0 : Math.Clamp(Zoom, MinZoom, MaxZoom);
        OffsetX = double.IsNaN(OffsetX) || double.IsInfinity(OffsetX) ? 0 : OffsetX;
        OffsetY = double.IsNaN(OffsetY) || double.IsInfinity(OffsetY) ? 0 : OffsetY;
        return this;
    }

    public CanvasViewport Clone() => new() { Zoom = Zoom, OffsetX = OffsetX, OffsetY = OffsetY };
}
