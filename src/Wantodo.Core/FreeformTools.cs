namespace Wantodo.Core;

/// <summary>
/// 自由表面的十个工具（0.7.0 P1.1）。
/// </summary>
/// <remarks>
/// 之所以放在 Core 而不是 App：工具 id、快捷键、显示名是「单一来源常量表」，
/// 设置页的只读快捷键表、工具条的互斥高亮、键盘分发都从这张表取，
/// 不能在 XAML 里再抄一份——抄一份就会漂。
/// </remarks>
public enum FreeformTool
{
    Select,
    Pan,
    Text,
    Image,
    Rect,
    Line,
    Arrow,
    Ink,
    Eraser,
    Zoom,
}

/// <summary>一个工具的三件事实：稳定 id（落盘/日志）、显示名、快捷键。</summary>
public sealed record FreeformToolInfo(FreeformTool Tool, string Id, string Name, char? Shortcut);

/// <summary>
/// 工具常量表。快捷键沿用画布类软件的习惯：V 选择、H 手形、T 文本、R 矩形、
/// A 箭头、B 画笔、E 橡皮；图片/线/缩放没有单键（避免和输入法抢占）。
/// </summary>
public static class FreeformToolCatalog
{
    public static IReadOnlyList<FreeformToolInfo> All { get; } =
    [
        new(FreeformTool.Select, "select", "选择", 'V'),
        new(FreeformTool.Pan, "pan", "手形", 'H'),
        new(FreeformTool.Text, "text", "文本", 'T'),
        new(FreeformTool.Image, "image", "图片", null),
        new(FreeformTool.Rect, "rect", "矩形", 'R'),
        new(FreeformTool.Line, "line", "线", null),
        new(FreeformTool.Arrow, "arrow", "箭头", 'A'),
        new(FreeformTool.Ink, "ink", "画笔", 'B'),
        new(FreeformTool.Eraser, "eraser", "橡皮", 'E'),
        new(FreeformTool.Zoom, "zoom", "缩放", null),
    ];

    public static int Count => All.Count;

    public static FreeformToolInfo Info(FreeformTool tool)
    {
        foreach (var info in All)
        {
            if (info.Tool == tool)
            {
                return info;
            }
        }

        return All[0];
    }

    public static string IdOf(FreeformTool tool) => Info(tool).Id;

    /// <summary>id 解析：未知/空 → 选择工具（不抛，坏数据不该让界面崩）。</summary>
    public static FreeformTool Parse(string? id, FreeformTool fallback = FreeformTool.Select)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return fallback;
        }

        foreach (var info in All)
        {
            if (string.Equals(info.Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return info.Tool;
            }
        }

        return fallback;
    }

    /// <summary>按键 → 工具（只认字母，忽略大小写）；没有对应工具的键返回 null。</summary>
    public static FreeformTool? FromShortcut(char key)
    {
        foreach (var info in All)
        {
            if (info.Shortcut is { } shortcut && char.ToUpperInvariant(shortcut) == char.ToUpperInvariant(key))
            {
                return info.Tool;
            }
        }

        return null;
    }
}

/// <summary>
/// 自由表面的缩放规则（25%–400%，每次 ×1.25）。
/// </summary>
/// <remarks>
/// 放在 Core 是为了让「两端可达」变成可断言的事实：从 100% 连按放大一定能到 400%，
/// 连按缩小一定能到 25%，中间不会出现 NaN、不会越界。
/// </remarks>
public static class FreeformZoom
{
    public const double Min = CanvasViewport.MinZoom;
    public const double Max = CanvasViewport.MaxZoom;
    public const double Step = 1.25;

    public static double Clamp(double zoom)
    {
        if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom <= 0)
        {
            return 1.0;
        }

        return Math.Clamp(zoom, Min, Max);
    }

    public static double Next(double zoom, bool zoomIn)
    {
        var current = Clamp(zoom);
        return Clamp(zoomIn ? current * Step : current / Step);
    }

    public static double Percent(double zoom) => Math.Round(Clamp(zoom) * 100);

    /// <summary>
    /// 「适应」：把内容外框（画布坐标）放进视口，留 24 DIP 边距；没有内容时回到 100%。
    /// </summary>
    public static double Fit(double contentWidth, double contentHeight, double viewportWidth, double viewportHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1.0;
        }

        const double padding = 24;
        var availableWidth = Math.Max(1, viewportWidth - (padding * 2));
        var availableHeight = Math.Max(1, viewportHeight - (padding * 2));
        return Clamp(Math.Min(availableWidth / contentWidth, availableHeight / contentHeight));
    }
}
