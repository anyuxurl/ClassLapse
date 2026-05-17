namespace ClassLapse.Models;

public sealed class CameraConfig
{
    public string DeviceMoniker { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    /// <summary>
    /// When true, the camera is opened at the highest resolution the driver reports
    /// and <see cref="Width"/>/<see cref="Height"/> are ignored.
    /// </summary>
    public bool UseHighestResolution { get; set; } = true;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public int JpegQuality { get; set; } = 85;
}
