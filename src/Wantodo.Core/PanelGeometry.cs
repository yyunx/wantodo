namespace Wantodo.Core;

/// <summary>面板可缩放的四条边（顶边 12 DIP 留给把手拖动，所以没有上边）。</summary>
public enum PanelEdge
{
    None,
    Left,
    Right,
    Bottom,
    BottomLeft,
    BottomRight,
}

/// <summary>面板尺寸（DIP）。</summary>
public readonly record struct PanelSize(double Width, double Height);

/// <summary>面板矩形（DIP，屏幕坐标）。</summary>
public readonly record struct PanelRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// 顶边面板的尺寸换算：工作区 → 窗口尺寸，窗口尺寸 → 记忆比例，边缘命中与拖拽夹取。
/// 纯函数，不依赖 WPF，主屏工作区的坐标由调用方传入（测试可直接喂 1366×728 之类的数字）。
/// </summary>
public static class PanelGeometry
{
    /// <summary>比例下限：240 DIP 在 1366×728 的工作区上约 0.33，0.2 只兜底极小屏。</summary>
    public const double MinRatio = 0.2;

    /// <summary>比例上限与「最大 90% 工作区」保持一致，避免存进去的比例和能拖到的高度对不上。</summary>
    public const double MaxRatio = 0.9;

    public const double DefaultRatio = 0.75;

    public const double MinWidth = 480;

    public const double MinHeight = 240;

    /// <summary>最大不超过工作区的 90%，避免拖成无边框全屏。</summary>
    public const double MaxWorkAreaFraction = 0.9;

    /// <summary>左右下三边与两个下角的命中带宽。</summary>
    public const double Grip = 6;

    /// <summary>顶边留给把手拖动，这段不做缩放。</summary>
    public const double TopReserved = 12;

    public static double ClampRatio(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultRatio;
        }

        return Math.Clamp(value, MinRatio, MaxRatio);
    }

    /// <summary>高度的唯一夹取入口：240 DIP 到工作区 90%，负值/NaN 也在这里兜住。</summary>
    public static double ClampHeight(PanelRect workArea, double height)
    {
        if (double.IsNaN(height) || double.IsInfinity(height))
        {
            return Math.Max(1, workArea.Height * MaxWorkAreaFraction);
        }

        double maxHeight = Math.Max(1, workArea.Height * MaxWorkAreaFraction);
        double minHeight = Math.Min(MinHeight, maxHeight);
        return Math.Clamp(height, minHeight, maxHeight);
    }

    /// <summary>把窗口高度换算成记忆比例；下限交给 <see cref="ClampHeight"/>，这里只挡负数与超大值。</summary>
    public static double HeightRatio(PanelRect workArea, double height)
    {
        if (workArea.Height <= 0)
        {
            return DefaultRatio;
        }

        double ratio = ClampHeight(workArea, height) / workArea.Height;
        return Math.Clamp(ratio, 0, MaxRatio);
    }

    /// <summary>展开后的窗口尺寸：比例 × 工作区，夹到最小尺寸与 90% 工作区之间。</summary>
    public static PanelSize ExpandedSize(PanelRect workArea, double widthRatio, double heightRatio)
    {
        double maxWidth = Math.Max(1, workArea.Width * MaxWorkAreaFraction);
        double maxHeight = Math.Max(1, workArea.Height * MaxWorkAreaFraction);

        double width = Math.Clamp(workArea.Width * ClampRatio(widthRatio), Math.Min(MinWidth, maxWidth), maxWidth);
        double heightRatioValue = double.IsNaN(heightRatio) || double.IsInfinity(heightRatio)
            ? DefaultRatio
            : Math.Clamp(heightRatio, 0, MaxRatio);
        double height = ClampHeight(workArea, workArea.Height * heightRatioValue);

        return new PanelSize(width, height);
    }

    /// <summary>水平居中贴在工作区顶边。</summary>
    public static PanelRect PlaceTopCenter(PanelRect workArea, PanelSize size)
    {
        return new PanelRect(
            workArea.Left + Math.Max(0, (workArea.Width - size.Width) / 2),
            workArea.Top,
            size.Width,
            size.Height);
    }

    /// <summary>把实际窗口尺寸换算回记忆比例（高度上下限与 <see cref="ClampHeight"/> 一致）。</summary>
    public static (double WidthRatio, double HeightRatio) RatiosFromSize(PanelRect workArea, PanelSize size)
    {
        double widthRatio = workArea.Width <= 0 ? DefaultRatio : size.Width / workArea.Width;
        return (ClampRatio(widthRatio), HeightRatio(workArea, size.Height));
    }

    /// <summary>
    /// 命中哪条边。坐标与窗口矩形同一空间（DIP）；顶边 12 DIP 内一律返回 <see cref="PanelEdge.None"/>，
    /// 把这一带留给「往下拖展开」。
    /// </summary>
    public static PanelEdge HitTest(PanelRect window, double x, double y, double grip = Grip, double topReserved = TopReserved)
    {
        if (window.Width <= 0 || window.Height <= 0)
        {
            return PanelEdge.None;
        }

        if (x < window.Left || x > window.Right || y < window.Top + topReserved || y > window.Bottom)
        {
            return PanelEdge.None;
        }

        bool left = x <= window.Left + grip;
        bool right = x >= window.Right - grip;
        bool bottom = y >= window.Bottom - grip;

        if (bottom && left)
        {
            return PanelEdge.BottomLeft;
        }

        if (bottom && right)
        {
            return PanelEdge.BottomRight;
        }

        return bottom ? PanelEdge.Bottom : left ? PanelEdge.Left : right ? PanelEdge.Right : PanelEdge.None;
    }

    /// <summary>
    /// 从按下时的矩形与位移算出新矩形：拖哪条边就只动那条边，顶边与左/右两侧的对边保持不动。
    /// 结果夹到最小 480×240、最大 90% 工作区，且不越出工作区左/上边界。
    /// </summary>
    public static PanelRect Resize(PanelRect start, PanelEdge edge, double dx, double dy, PanelRect workArea)
    {
        double maxWidth = Math.Max(1, workArea.Width * MaxWorkAreaFraction);
        double maxHeight = Math.Max(1, workArea.Height * MaxWorkAreaFraction);
        // 宽度下限与比例下限保持一致；高度统一走 ClampHeight，避免「拖到最小」与「存回比例」各算一套。
        double minWidth = Math.Min(Math.Max(MinWidth, workArea.Width * MinRatio), maxWidth);

        double left = start.Left;
        double top = start.Top;
        double width = start.Width;
        double height = start.Height;

        if (edge is PanelEdge.Left or PanelEdge.BottomLeft)
        {
            width = Math.Clamp(start.Width - dx, minWidth, maxWidth);
            double maxLeft = Math.Max(workArea.Left, start.Right - minWidth);
            left = Math.Clamp(start.Right - width, workArea.Left, maxLeft);
        }
        else if (edge is PanelEdge.Right or PanelEdge.BottomRight)
        {
            width = Math.Clamp(start.Width + dx, minWidth, maxWidth);
            left = Math.Clamp(start.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        }

        if (edge is PanelEdge.Bottom or PanelEdge.BottomLeft or PanelEdge.BottomRight)
        {
            height = ClampHeight(workArea, start.Height + dy);
        }

        return new PanelRect(left, top, width, height);
    }
}
