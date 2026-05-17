namespace ClassLapse.Models;

public sealed record CameraDevice(string MonikerString, string FriendlyName)
{
    public override string ToString() => FriendlyName;
}
