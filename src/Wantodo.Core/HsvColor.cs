namespace Wantodo.Core;

/// <summary>
/// HSV（色相 0–360，饱和度/明度 0–1）与 <see cref="Rgb"/> 的互转。
/// 取色器需要「色相条 + 饱和度/明度方块」，而 <see cref="ThemePalette"/> 只认 RGB，
/// 所以把这段公式放 Core 里，单测能直接验，不依赖 WPF。
/// </summary>
public readonly record struct HsvColor(double Hue, double Saturation, double Value)
{
    public static HsvColor FromRgb(Rgb color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        double saturation = max <= 0 ? 0 : delta / max;
        return new HsvColor(hue, saturation, max);
    }

    public Rgb ToRgb()
    {
        double hue = Hue % 360;
        if (hue < 0)
        {
            hue += 360;
        }

        double saturation = Math.Clamp(Saturation, 0, 1);
        double value = Math.Clamp(Value, 0, 1);

        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;

        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return new Rgb(Channel(r + m), Channel(g + m), Channel(b + m));

        static byte Channel(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }
}
