using AForge.Video.DirectShow;
using ClassLapse.Models;

namespace ClassLapse.Core;

public static class CameraEnumerator
{
    public static IReadOnlyList<CameraDevice> Enumerate()
    {
        var filters = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        var devices = new List<CameraDevice>(filters.Count);
        foreach (FilterInfo f in filters)
        {
            devices.Add(new CameraDevice(f.MonikerString, f.Name));
        }
        return devices;
    }

    public static CameraDevice? FindByMoniker(string monikerString)
    {
        foreach (var d in Enumerate())
        {
            if (d.MonikerString == monikerString) return d;
        }
        return null;
    }
}
