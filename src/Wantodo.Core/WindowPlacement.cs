namespace Wantodo.Core;

/// <summary>窗口的位置与尺寸，单位是 WPF 的 DIP（不是像素），这样换 DPI 不会跑位。</summary>
public sealed class WindowPlacement
{
    /// <summary>null = 还没存过位置，由程序挑一个默认落点（也不能用 NaN 存，JSON 不支持）。</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 560;

    public double Height { get; set; } = 320;

    /// <summary>
    /// 窗口尺寸策略：<see cref="SizeModes.AutoFit"/>（默认，结构变化时跟着收放）
    /// 或 <see cref="SizeModes.Manual"/>（用户自己调过，之后不再自动改）。
    /// </summary>
    public string SizeMode { get; set; } = SizeModes.AutoFit;

    /// <summary>有没有存过位置。没存过就由程序挑一个默认落点。派生值，不写进 JSON。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPosition => Left.HasValue && Top.HasValue;
}
