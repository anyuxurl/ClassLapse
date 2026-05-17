namespace ClassLapse.Models;

public sealed class CameraConfig
{
    public string DeviceMoniker { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public int JpegQuality { get; set; } = 85;
}
