namespace Wantodo.Core.Tests;

using Wantodo.Core;

/// <summary>取色器靠 HSV ↔ RGB 互转；这里验已知色、灰色与随机色的往返误差。</summary>
public class HsvColorTests
{
    [Theory]
    [InlineData(0xFF, 0x00, 0x00, 0)]     // 红
    [InlineData(0xFF, 0xFF, 0x00, 60)]    // 黄
    [InlineData(0x00, 0xFF, 0x00, 120)]   // 绿
    [InlineData(0x00, 0xFF, 0xFF, 180)]   // 青
    [InlineData(0x00, 0x00, 0xFF, 240)]   // 蓝
    [InlineData(0xFF, 0x00, 0xFF, 300)]   // 品红
    public void FromRgb_ReportsExpectedHue(byte r, byte g, byte b, double hue)
    {
        var hsv = HsvColor.FromRgb(new Rgb(r, g, b));

        Assert.Equal(hue, hsv.Hue, 2);
        Assert.Equal(1, hsv.Saturation, 2);
        Assert.Equal(1, hsv.Value, 2);
    }

    [Fact]
    public void FromRgb_WhiteIsZeroSaturation()
    {
        var hsv = HsvColor.FromRgb(new Rgb(255, 255, 255));

        Assert.Equal(0, hsv.Saturation, 3);
        Assert.Equal(1, hsv.Value, 3);
    }

    [Fact]
    public void FromRgb_BlackIsZeroValue()
    {
        var hsv = HsvColor.FromRgb(new Rgb(0, 0, 0));

        Assert.Equal(0, hsv.Value, 3);
        Assert.Equal(new Rgb(0, 0, 0), hsv.ToRgb());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(240)]
    [InlineData(300)]
    public void ToRgb_FullSaturationAndValue_StaysInGamut(double hue)
    {
        var color = new HsvColor(hue, 1, 1).ToRgb();

        Assert.InRange(color.R, 0, 255);
        Assert.InRange(color.G, 0, 255);
        Assert.InRange(color.B, 0, 255);
    }

    [Fact]
    public void RoundTrip_TwoHundredRandomColors_WithinOneStep()
    {
        var random = new Random(20261114);

        for (var i = 0; i < 200; i++)
        {
            var original = new Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            var roundTripped = HsvColor.FromRgb(original).ToRgb();

            Assert.InRange(Math.Abs(roundTripped.R - original.R), 0, 1);
            Assert.InRange(Math.Abs(roundTripped.G - original.G), 0, 1);
            Assert.InRange(Math.Abs(roundTripped.B - original.B), 0, 1);
        }
    }
}
