using System;
using System.IO;
using System.Linq;
using ClassLapse.Core;
using Xunit;

namespace ClassLapse.Tests;

public class CaptureLibraryTests : IDisposable
{
    private readonly string _root;

    public CaptureLibraryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cl-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private string Day(string date)
    {
        var d = Path.Combine(_root, date);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Jpg(string dir, string name, int bytes = 16)
        => File.WriteAllBytes(Path.Combine(dir, name), new byte[bytes]);

    [Fact]
    public void EnumerateDays_lists_date_folders_sorted_with_nonempty_counts()
    {
        var d18 = Day("2026-05-18");
        Jpg(d18, "08-00-00.jpg");
        Jpg(d18, "08-00-30.jpg");
        Jpg(d18, "empty.jpg", bytes: 0); // zero-byte = failed capture, excluded from count
        var d17 = Day("2026-05-17");
        Jpg(d17, "09-00-00.jpg");
        Day("2026-05-19");          // valid date, no photos
        Day("not-a-date");          // ignored entirely

        var days = CaptureLibrary.EnumerateDays(_root);

        Assert.Equal(3, days.Count);
        Assert.Equal(new DateOnly(2026, 5, 17), days[0].Date); // sorted ascending
        Assert.Equal(new DateOnly(2026, 5, 18), days[1].Date);
        Assert.Equal(new DateOnly(2026, 5, 19), days[2].Date);
        Assert.Equal(2, days.First(d => d.Date == new DateOnly(2026, 5, 18)).JpgCount); // empty.jpg not counted
        Assert.Equal(0, days.First(d => d.Date == new DateOnly(2026, 5, 19)).JpgCount);
    }

    [Fact]
    public void CollectFrames_orders_by_day_then_ordinal_name_and_skips_empty()
    {
        var d18 = Day("2026-05-18");
        Jpg(d18, "08-00-30.jpg");
        Jpg(d18, "08-00-00.jpg");
        Jpg(d18, "bad.jpg", bytes: 0);
        var d17 = Day("2026-05-17");
        Jpg(d17, "23-59-00.jpg");

        var frames = CaptureLibrary.CollectFrames(CaptureLibrary.EnumerateDays(_root));

        Assert.Equal(3, frames.Count);
        Assert.EndsWith(Path.Combine("2026-05-17", "23-59-00.jpg"), frames[0]); // earlier day first
        Assert.EndsWith(Path.Combine("2026-05-18", "08-00-00.jpg"), frames[1]); // name-sorted within day
        Assert.EndsWith(Path.Combine("2026-05-18", "08-00-30.jpg"), frames[2]);
    }

    [Fact]
    public void EnumerateDays_handles_missing_or_empty_input()
    {
        Assert.Empty(CaptureLibrary.EnumerateDays(Path.Combine(_root, "does-not-exist")));
        Assert.Empty(CaptureLibrary.EnumerateDays(null));
        Assert.Empty(CaptureLibrary.EnumerateDays(""));
    }
}
