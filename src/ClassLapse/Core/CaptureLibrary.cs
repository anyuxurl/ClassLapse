using System.Globalization;
using System.IO;

namespace ClassLapse.Core;

/// <summary>
/// Read-only view over the <c>{OutputFolder}/yyyy-MM-dd/*.jpg</c> capture tree. The timelapse
/// composer uses it to list available days and gather frames in chronological order.
/// Zero-byte files (failed captures) and non-jpg files are excluded so the count shown to the
/// user equals the number of frames that will actually be encoded.
/// </summary>
public static class CaptureLibrary
{
    public sealed record CaptureDay(DateOnly Date, string Path, int JpgCount, long SizeBytes);

    /// <summary>Day folders whose name parses as yyyy-MM-dd, each with its non-empty .jpg count and total size, sorted by date ascending.</summary>
    public static IReadOnlyList<CaptureDay> EnumerateDays(string? outputFolder)
    {
        var days = new List<CaptureDay>();
        if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
        {
            return days;
        }

        foreach (var path in SafeEnumerateDirectories(outputFolder))
        {
            var name = Path.GetFileName(path);
            if (!DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var date))
            {
                continue;
            }

            int count = 0;
            long size = 0;
            foreach (var f in SafeEnumerateJpgs(path))
            {
                long len = SafeLength(f);
                if (len <= 0) continue;
                count++;
                size += len;
            }
            days.Add(new CaptureDay(date, path, count, size));
        }

        days.Sort((a, b) => a.Date.CompareTo(b.Date));
        return days;
    }

    /// <summary>
    /// Every non-empty .jpg frame path across the given days, in chronological order: days by
    /// date, files within a day by ordinal name (<c>HH-mm-ss-fff.jpg</c> sorts == capture time).
    /// </summary>
    public static IReadOnlyList<string> CollectFrames(IEnumerable<CaptureDay> days)
    {
        var frames = new List<string>();
        foreach (var day in days.OrderBy(d => d.Date))
        {
            var inDay = new List<string>();
            foreach (var f in SafeEnumerateJpgs(day.Path))
            {
                if (SafeLength(f) > 0) inDay.Add(f);
            }
            inDay.Sort(StringComparer.Ordinal);
            frames.AddRange(inDay);
        }
        return frames;
    }

    private static List<string> SafeEnumerateDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch { return new List<string>(); }
    }

    private static List<string> SafeEnumerateJpgs(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.jpg", SearchOption.TopDirectoryOnly).ToList(); }
        catch { return new List<string>(); }
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return -1; }
    }
}
