namespace Wantodo.Core;

/// <summary>
/// 窗口位置的兜底计算。拔掉副屏、改分辨率、换缩放之后，保存的位置可能整个落在屏幕外，
/// 用户会以为便签丢了。这里只做纯计算，方便单测（真正的屏幕边界由调用方传进来）。
/// </summary>
public static class PlacementGuard
{
    /// <summary>整个窗口是否都在给定矩形内。</summary>
    public static bool IsFullyInside(
        double left,
        double top,
        double width,
        double height,
        double screenLeft,
        double screenTop,
        double screenRight,
        double screenBottom)
    {
        return left >= screenLeft
            && top >= screenTop
            && left + width <= screenRight
            && top + height <= screenBottom;
    }

    /// <summary>
    /// 把左上角拉回可见区域：
    /// 窗口放得下时，保证整个窗口都在矩形内（不留"只剩一条边"的状态）；
    /// 窗口比矩形还大时，贴到左上角，优先保证标题栏和左边缘可见。
    /// </summary>
    public static (double Left, double Top) Clamp(
        double left,
        double top,
        double width,
        double height,
        double screenLeft,
        double screenTop,
        double screenRight,
        double screenBottom)
    {
        double screenWidth = screenRight - screenLeft;
        double screenHeight = screenBottom - screenTop;

        double clampedLeft = width >= screenWidth
            ? screenLeft
            : Math.Clamp(left, screenLeft, screenRight - width);

        double clampedTop = height >= screenHeight
            ? screenTop
            : Math.Clamp(top, screenTop, screenBottom - height);

        return (clampedLeft, clampedTop);
    }
}