using System;
using System.Collections.Generic;
using System.Linq;
using ClassLapse.Core;
using Xunit;

namespace ClassLapse.Tests;

public class FrameResamplerTests
{
    [Theory]
    [InlineData("19-10-02.jpg", 19, 10, 2, 0)]
    [InlineData("09-01-57-479.jpg", 9, 1, 57, 479)]
    [InlineData("00-00-00.jpg", 0, 0, 0, 0)]
    public void ParseTimeOfDay_parses_hms_with_optional_ms(string name, int h, int m, int s, int ms)
    {
        var t = FrameResampler.ParseTimeOfDay(name);
        Assert.NotNull(t);
        Assert.Equal(new TimeSpan(0, h, m, s, ms), t!.Value);
    }

    [Theory]
    [InlineData("frame.jpg")]      // no timestamp
    [InlineData("08-99-00.jpg")]   // minute out of range
    [InlineData("25-00-00.jpg")]   // hour out of range
    [InlineData("08-00.jpg")]      // too few parts
    public void ParseTimeOfDay_returns_null_for_unparseable(string name)
    {
        Assert.Null(FrameResampler.ParseTimeOfDay(name));
    }

    [Fact]
    public void ResampleDay_keeps_first_then_one_per_cadence_on_a_dense_day()
    {
        // A frame every 30s from 08:00:00 to 08:10:00 (21 frames).
        var frames = Enumerable.Range(0, 21)
            .Select(i => TimeSpan.FromSeconds(i * 30))
            .Select(t => $"08-{t.Minutes:00}-{t.Seconds:00}.jpg")
            .ToList();

        var kept = FrameResampler.ResampleDay(frames, TimeSpan.FromMinutes(5));

        // First always kept, then next only once >= 5 min since the last KEPT frame.
        Assert.Equal(new[] { "08-00-00.jpg", "08-05-00.jpg", "08-10-00.jpg" }, kept);
    }

    [Fact]
    public void ResampleDay_leaves_a_sparse_day_untouched()
    {
        var frames = new[] { "07-15-00.jpg", "07-45-00.jpg", "08-15-00.jpg" }; // 30 min apart
        var kept = FrameResampler.ResampleDay(frames, TimeSpan.FromMinutes(5));
        Assert.Equal(frames, kept);
    }

    [Fact]
    public void ResampleDay_keeps_untimestamped_frames_but_still_thins_timed_ones()
    {
        var frames = new[] { "08-00-00.jpg", "note.txt", "08-00-30.jpg" };
        var kept = FrameResampler.ResampleDay(frames, TimeSpan.FromMinutes(5));
        Assert.Contains("note.txt", kept);           // fail open — never silently dropped
        Assert.Contains("08-00-00.jpg", kept);        // first kept
        Assert.DoesNotContain("08-00-30.jpg", kept);  // 30s < 5min, thinned
    }

    [Fact]
    public void ResampleDay_nonpositive_cadence_returns_input_unchanged()
    {
        var frames = new[] { "08-00-00.jpg", "08-00-30.jpg" };
        Assert.Same(frames, FrameResampler.ResampleDay(frames, TimeSpan.Zero));
    }

    [Fact]
    public void Resample_resamples_each_day_independently()
    {
        var day1 = new[] { "08-00-00.jpg", "08-00-30.jpg", "08-06-00.jpg" };
        var day2 = new[] { "09-00-00.jpg", "09-02-00.jpg" };

        var kept = FrameResampler.Resample(new IReadOnlyList<string>[] { day1, day2 }, TimeSpan.FromMinutes(5));

        // day1: keep 08-00-00, drop 08-00-30 (30s), keep 08-06-00 (6min);
        // day2: independent — keep 09-00-00, drop 09-02-00 (2min).
        Assert.Equal(new[] { "08-00-00.jpg", "08-06-00.jpg", "09-00-00.jpg" }, kept);
    }
}
