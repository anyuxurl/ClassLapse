using System;
using System.Linq;
using ClassLapse.Core;
using ClassLapse.Models;
using Xunit;

namespace ClassLapse.Tests;

public class FfmpegCommandTests
{
    // ----- ToConcatLine -----

    [Fact]
    public void ToConcatLine_uses_forward_slashes_and_single_quotes()
    {
        Assert.Equal("file 'D:/cap/2026-05-18/08-00-00.jpg'",
            FfmpegCommand.ToConcatLine(@"D:\cap\2026-05-18\08-00-00.jpg"));
    }

    [Fact]
    public void ToConcatLine_keeps_spaces_and_non_ascii()
    {
        Assert.Equal("file 'D:/My Caps/课堂/x.jpg'",
            FfmpegCommand.ToConcatLine(@"D:\My Caps\课堂\x.jpg"));
    }

    [Fact]
    public void ToConcatLine_escapes_single_quote()
    {
        // ' -> '\''  : D:/Bob'\''s/x.jpg
        Assert.Equal("file 'D:/Bob'\\''s/x.jpg'",
            FfmpegCommand.ToConcatLine(@"D:\Bob's\x.jpg"));
    }

    // ----- BuildList -----

    [Fact]
    public void BuildList_primary_is_bare_file_lines()
    {
        var list = FfmpegCommand.BuildList(new[] { @"C:\a\1.jpg", @"C:\a\2.jpg" }, useDurations: false, 1.0 / 30);
        var lines = list.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[]
        {
            "ffconcat version 1.0",
            "file 'C:/a/1.jpg'",
            "file 'C:/a/2.jpg'",
        }, lines);
    }

    [Fact]
    public void BuildList_fallback_has_durations_and_repeats_last()
    {
        var list = FfmpegCommand.BuildList(new[] { @"C:\a\1.jpg", @"C:\a\2.jpg" }, useDurations: true, 0.5);
        var lines = list.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[]
        {
            "ffconcat version 1.0",
            "file 'C:/a/1.jpg'",
            "duration 0.5",
            "file 'C:/a/2.jpg'",
            "duration 0.5",
            "file 'C:/a/2.jpg'", // last repeated so its frame isn't dropped
        }, lines);
    }

    // ----- BuildArgs -----

    [Fact]
    public void BuildArgs_primary_puts_input_rate_before_input()
    {
        var cfg = new TimelapseConfig { Fps = 24, ResolutionHeight = 1080, Crf = 20, UseDurationListFallback = false };
        var args = FfmpegCommand.BuildArgs("list.txt", "out.mp4", cfg, hasLibx264: true).ToList();

        int rIdx = args.IndexOf("-r");
        int iIdx = args.IndexOf("-i");
        Assert.True(rIdx >= 0 && rIdx < iIdx, "-r must come before -i in primary mode");
        Assert.Equal("24", args[rIdx + 1]);
        Assert.Equal(1, args.Count(a => a == "-r"));
        Assert.Contains("libx264", args);
        Assert.Contains("-crf", args);
        Assert.Contains("scale=-2:1080,format=yuv420p", args);
        Assert.Contains("+faststart", args);
    }

    [Fact]
    public void BuildArgs_fallback_puts_rate_after_input_only()
    {
        var cfg = new TimelapseConfig { Fps = 30, UseDurationListFallback = true };
        var args = FfmpegCommand.BuildArgs("list.txt", "out.mp4", cfg, hasLibx264: true).ToList();

        int rIdx = args.IndexOf("-r");
        int iIdx = args.IndexOf("-i");
        Assert.True(rIdx > iIdx, "-r must come after -i in fallback mode");
        Assert.Equal(1, args.Count(a => a == "-r"));
    }

    [Fact]
    public void BuildArgs_uses_mpeg4_when_libx264_absent()
    {
        var args = FfmpegCommand.BuildArgs("l", "o", new TimelapseConfig(), hasLibx264: false).ToList();

        Assert.Contains("mpeg4", args);
        Assert.DoesNotContain("libx264", args);
        Assert.Contains("-q:v", args);
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void BuildScaleFilter_height_vs_original()
    {
        Assert.Equal("scale=-2:720,format=yuv420p", FfmpegCommand.BuildScaleFilter(720));
        Assert.Equal("scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p", FfmpegCommand.BuildScaleFilter(0));
    }
}
