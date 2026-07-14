using System.Drawing;
using ClassLapse.Core;
using ClassLapse.Models;
using Xunit;

namespace ClassLapse.Tests;

public class WatermarkTests
{
    // ----- pure helpers (TimestampWatermark) -----

    [Fact]
    public void ResolveFontSizePx_auto_scales_with_height_with_a_floor()
    {
        Assert.Equal(36, TimestampWatermark.ResolveFontSizePx(1080, 0)); // 1080/30
        Assert.Equal(12, TimestampWatermark.ResolveFontSizePx(100, 0));  // floored to legible min
        Assert.Equal(50, TimestampWatermark.ResolveFontSizePx(1080, 50)); // explicit wins
    }

    [Fact]
    public void FormatTimestamp_formats_valid_and_falls_back_on_invalid()
    {
        var ts = new DateTime(2026, 5, 17, 14, 30, 5);
        Assert.Equal("2026-05-17 14:30:05", TimestampWatermark.FormatTimestamp(ts, "yyyy-MM-dd HH:mm:ss"));
        Assert.Equal("14:30", TimestampWatermark.FormatTimestamp(ts, "HH:mm"));

        // An unterminated literal quote is an invalid format → fall back to the default, never throw.
        Assert.Equal("2026-05-17 14:30:05", TimestampWatermark.FormatTimestamp(ts, "'unterminated"));
        Assert.Equal("2026-05-17 14:30:05", TimestampWatermark.FormatTimestamp(ts, ""));
    }

    [Fact]
    public void ResolvePosition_places_each_corner()
    {
        var text = new SizeF(100, 20);
        var image = new Size(1000, 1000);
        const float margin = 10;

        Assert.Equal(new PointF(10, 10), TimestampWatermark.ResolvePosition(WatermarkPosition.TopLeft, text, image, margin));
        Assert.Equal(new PointF(890, 10), TimestampWatermark.ResolvePosition(WatermarkPosition.TopRight, text, image, margin));
        Assert.Equal(new PointF(10, 970), TimestampWatermark.ResolvePosition(WatermarkPosition.BottomLeft, text, image, margin));
        Assert.Equal(new PointF(890, 970), TimestampWatermark.ResolvePosition(WatermarkPosition.BottomRight, text, image, margin));
    }

    [Fact]
    public void ParseColor_parses_hex_and_falls_back()
    {
        var red = TimestampWatermark.ParseColor("#FF0000", Color.White);
        Assert.Equal(255, red.R);
        Assert.Equal(0, red.G);
        Assert.Equal(0, red.B);

        Assert.Equal(Color.White.ToArgb(), TimestampWatermark.ParseColor("not-a-color", Color.White).ToArgb());
        Assert.Equal(Color.White.ToArgb(), TimestampWatermark.ParseColor("", Color.White).ToArgb());
    }

    // ----- GDI drawing (Windows; same platform as the app) -----

    [Fact]
    public void Draw_writes_pixels_in_the_target_corner_only()
    {
        using var bmp = new Bitmap(400, 200);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);

        var cfg = new WatermarkConfig
        {
            Enabled = true,
            Position = WatermarkPosition.BottomRight,
            FontSize = 28,
            Format = "HH:mm:ss",
            Color = "#FFFFFF",
            Outline = true,
        };
        TimestampWatermark.Draw(bmp, new DateTime(2026, 5, 17, 14, 30, 5), cfg);

        Assert.True(HasNonWhite(bmp, new Rectangle(200, 100, 200, 100)), "bottom-right corner should carry text");
        Assert.False(HasNonWhite(bmp, new Rectangle(0, 0, 150, 80)), "top-left corner should be untouched");
    }

    // ----- helpers -----

    private static bool HasNonWhite(Bitmap bmp, Rectangle r)
    {
        for (int y = r.Top; y < r.Bottom; y++)
        {
            for (int x = r.Left; x < r.Right; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.R < 250 || c.G < 250 || c.B < 250) return true;
            }
        }
        return false;
    }
}
