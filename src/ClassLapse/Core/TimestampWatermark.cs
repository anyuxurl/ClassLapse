using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.Versioning;
using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// Burns a timestamp watermark onto a <see cref="Bitmap"/> in place, before JPEG encoding.
/// Drawing uses System.Drawing (already a dependency via <see cref="CameraService"/>).
/// The geometry/format/colour helpers are pure so they can be unit-tested without a real font.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TimestampWatermark
{
    private const string DefaultFormat = "yyyy-MM-dd HH:mm:ss";
    private const int MinFontPx = 12;

    /// <summary>Draw <paramref name="ts"/> formatted per <paramref name="cfg"/> onto <paramref name="bmp"/>.</summary>
    public static void Draw(Bitmap bmp, DateTime ts, WatermarkConfig cfg)
    {
        string text = FormatTimestamp(ts, cfg.Format);
        if (string.IsNullOrEmpty(text)) return;

        int fontPx = ResolveFontSizePx(bmp.Height, cfg.FontSize);
        Color color = ParseColor(cfg.Color, Color.White);

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Consolas is monospaced so digits never jitter between frames; GDI+ substitutes a
        // default face automatically if the family is unavailable on the target machine.
        using var font = new Font("Consolas", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);

        SizeF textSize = g.MeasureString(text, font);
        float margin = fontPx * 0.6f;
        PointF at = ResolvePosition(cfg.Position, textSize, new Size(bmp.Width, bmp.Height), margin);

        if (cfg.Outline)
        {
            int o = Math.Max(1, fontPx / 14);
            using var outline = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
            for (int dx = -o; dx <= o; dx += o)
            {
                for (int dy = -o; dy <= o; dy += o)
                {
                    if (dx == 0 && dy == 0) continue;
                    g.DrawString(text, font, outline, at.X + dx, at.Y + dy);
                }
            }
        }

        using var fill = new SolidBrush(color);
        g.DrawString(text, font, fill, at.X, at.Y);
    }

    /// <summary>Resolve the pixel font size: a positive configured value wins, else ~image height/30 floored to a legible minimum.</summary>
    public static int ResolveFontSizePx(int imageHeight, int configured)
    {
        if (configured > 0) return configured;
        return Math.Max(MinFontPx, imageHeight / 30);
    }

    /// <summary>Format <paramref name="ts"/>, falling back to the default format if the string is empty or invalid (never throws).</summary>
    public static string FormatTimestamp(DateTime ts, string format)
    {
        if (string.IsNullOrEmpty(format)) format = DefaultFormat;
        try
        {
            return ts.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return ts.ToString(DefaultFormat, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Top-left draw point for the given corner, with <paramref name="margin"/> inset, clamped to stay on-image.</summary>
    public static PointF ResolvePosition(WatermarkPosition pos, SizeF text, Size image, float margin)
    {
        float left = margin;
        float top = margin;
        float right = image.Width - text.Width - margin;
        float bottom = image.Height - text.Height - margin;

        (float x, float y) = pos switch
        {
            WatermarkPosition.TopLeft => (left, top),
            WatermarkPosition.TopRight => (right, top),
            WatermarkPosition.BottomLeft => (left, bottom),
            _ => (right, bottom), // BottomRight
        };

        return new PointF(Math.Max(0, x), Math.Max(0, y));
    }

    /// <summary>Parse an HTML hex colour, returning <paramref name="fallback"/> when empty or invalid.</summary>
    public static Color ParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            return ColorTranslator.FromHtml(hex.Trim());
        }
        catch
        {
            return fallback;
        }
    }
}
