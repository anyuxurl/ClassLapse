using System.Globalization;
using System.IO;

namespace ClassLapse.Core;

/// <summary>
/// Timestamp-aware frame selection. A capture's filename IS its wall-clock time
/// (<c>HH-mm-ss</c> or <c>HH-mm-ss-fff</c>), so we can thin dense days down to a uniform
/// real-time cadence and leave sparse days untouched — making output screen-time roughly
/// proportional to real elapsed time instead of to how many photos happened to be taken.
///
/// Each day is resampled independently (its first frame is always kept), so the long night
/// gap between two days is never treated as elapsed time to bridge.
/// </summary>
public static class FrameResampler
{
    /// <summary>
    /// Parse the time-of-day from a frame path's filename. Accepts <c>HH-mm-ss.jpg</c> and
    /// <c>HH-mm-ss-fff.jpg</c> (fff = milliseconds). Returns null if the name doesn't match,
    /// so callers can decide how to treat un-timestamped frames.
    /// </summary>
    public static TimeSpan? ParseTimeOfDay(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('-');
        if (parts.Length < 3) return null;

        if (!TryInt(parts[0], out int hh) || hh is < 0 or > 23) return null;
        if (!TryInt(parts[1], out int mm) || mm is < 0 or > 59) return null;
        if (!TryInt(parts[2], out int ss) || ss is < 0 or > 59) return null;

        int ms = 0;
        if (parts.Length >= 4 && (!TryInt(parts[3], out ms) || ms is < 0 or > 999)) return null;

        return new TimeSpan(0, hh, mm, ss, ms);
    }

    /// <summary>
    /// Keep ~one frame per <paramref name="cadence"/> of real time within a single day's frames
    /// (given in chronological order). The first frame is always kept; each subsequent frame is
    /// kept only once at least <paramref name="cadence"/> has elapsed since the last KEPT frame,
    /// so days sampled sparser than the cadence pass through unchanged. Frames whose name has no
    /// parseable time are kept (fail open). A non-positive cadence returns the input unchanged.
    /// </summary>
    public static IReadOnlyList<string> ResampleDay(IReadOnlyList<string> dayFramesChronological, TimeSpan cadence)
    {
        if (cadence <= TimeSpan.Zero || dayFramesChronological.Count == 0)
        {
            return dayFramesChronological;
        }

        var kept = new List<string>();
        TimeSpan? lastKept = null;
        foreach (var frame in dayFramesChronological)
        {
            var t = ParseTimeOfDay(frame);
            if (t == null)
            {
                kept.Add(frame); // un-timestamped: don't silently drop it
                continue;
            }
            if (lastKept == null || t.Value - lastKept.Value >= cadence)
            {
                kept.Add(frame);
                lastKept = t.Value;
            }
        }
        return kept;
    }

    /// <summary>Flatten per-day frame lists, resampling each day to <paramref name="cadence"/>.</summary>
    public static IReadOnlyList<string> Resample(
        IEnumerable<IReadOnlyList<string>> framesByDay, TimeSpan cadence)
    {
        var result = new List<string>();
        foreach (var day in framesByDay)
        {
            result.AddRange(ResampleDay(day, cadence));
        }
        return result;
    }

    private static bool TryInt(string s, out int value)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
