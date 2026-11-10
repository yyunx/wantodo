namespace Wantodo.Core;

/// <summary>窗口尺寸策略。存在 <see cref="WindowPlacement.SizeMode"/> 里，是字符串，方便手改 JSON。</summary>
public static class SizeModes
{
    /// <summary>结构变了（加栏/删栏/停靠）就让窗口跟着收放。</summary>
    public const string AutoFit = "AutoFit";

    /// <summary>用户自己调过窗口，之后不再擅自改尺寸。</summary>
    public const string Manual = "Manual";

    public static bool IsValid(string? mode) =>
        string.Equals(mode, AutoFit, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mode, Manual, StringComparison.OrdinalIgnoreCase);

    /// <summary>归一成规范的字符串：只认 Manual（大小写不敏感），其它一律 AutoFit。</summary>
    public static string Normalize(string? mode) =>
        string.Equals(mode, Manual, StringComparison.OrdinalIgnoreCase) ? Manual : AutoFit;
}

/// <summary>
/// 窗口宽度与栏数的换算。纯函数，不碰 WPF，方便单测。
/// </summary>
/// <remarks>
/// 口径来自 XAML：外层 <c>ScrollViewer</c> 左右各 8 DIP 边距（共 16），
/// 每栏模板左右各 4 DIP 边距（相邻两栏之间是 8）。
/// 所以 n 栏时窗口宽度 = 16 + n × (栏宽 + 8)。
/// </remarks>
public static class LayoutMetrics
{
    /// <summary>一栏希望占的舒适宽度（DIP）。</summary>
    public const double PreferredPaneWidth = 280;

    /// <summary>一栏能压到的最窄宽度（DIP）。</summary>
    public const double MinPaneWidth = 190;

    /// <summary>相邻两栏之间的间距（DIP）。</summary>
    public const double PaneGap = 8;

    /// <summary>窗口左右两侧的外框开销（ScrollViewer 的 8+8）。</summary>
    public const double ChromeWidth = 16;

    /// <summary>n 栏、每栏按舒适宽度算，窗口该有多宽。</summary>
    public static double ComputePreferredWidth(int paneCount) =>
        ChromeWidth + Math.Max(0, paneCount) * (PreferredPaneWidth + PaneGap);

    /// <summary>n 栏、每栏按最窄算，窗口至少要有多宽。</summary>
    public static double ComputeMinWidth(int paneCount) =>
        ChromeWidth + Math.Max(0, paneCount) * (MinPaneWidth + PaneGap);

    /// <summary>
    /// 把期望宽度夹到「能放得下全部栏的最小宽度」与「工作区宽度」之间。
    /// 工作区比最小宽度还窄时，以最小宽度为准（宁可出滚动条，也不把栏压成缝）。
    /// </summary>
    public static double FitTo(double width, int paneCount, double workAreaWidth)
    {
        double min = ComputeMinWidth(paneCount);
        double max = Math.Max(min, workAreaWidth);
        return Math.Clamp(width, min, max);
    }

    /// <summary>给定可用宽度，最多能放下几栏（每栏按最窄算）。</summary>
    public static int ComputeMaxPanes(double availableWidth)
    {
        if (availableWidth <= 0 || double.IsNaN(availableWidth))
        {
            return 1;
        }

        // 可用宽度 = 16 + n × (190 + 8) → n = (可用 - 16) / 198
        int count = (int)Math.Floor((availableWidth - ChromeWidth) / (MinPaneWidth + PaneGap));
        return Math.Max(1, count);
    }
}
