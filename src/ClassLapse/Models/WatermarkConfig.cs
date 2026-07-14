namespace ClassLapse.Models;

public sealed class WatermarkConfig
{
    /// <summary>Burn the capture timestamp into each photo (before JPEG encode). Default on — timelapse material wants it.</summary>
    public bool Enabled { get; set; } = true;

    public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;

    /// <summary>.NET <see cref="System.DateTime"/> format string. A bad string falls back to the default at render time, never throws.</summary>
    public string Format { get; set; } = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Font size in pixels. 0 = auto (~image height / 30, floored to a legible minimum).</summary>
    public int FontSize { get; set; } = 0;

    /// <summary>Text color as an HTML hex string (e.g. <c>#FFFFFF</c>). Falls back to white if unparseable.</summary>
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>Draw a dark outline behind the text so it stays legible on any background.</summary>
    public bool Outline { get; set; } = true;
}
