using System;
using System.Collections.Generic;
using System.IO;
using ClassLapse.Core;
using Xunit;

namespace ClassLapse.Tests;

public class FfmpegLocatorTests
{
    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(p);
    }

    private static readonly string BesideDir = Path.Combine("app", "dir");
    private static readonly string BesideExe = Path.Combine(BesideDir, "ffmpeg.exe");
    private static readonly string PathDir = Path.Combine("usr", "bin");
    private static readonly string PathExe = Path.Combine(PathDir, "ffmpeg.exe");

    [Fact]
    public void Configured_path_to_the_exe_wins()
    {
        var cfg = Path.Combine("custom", "ff.exe");

        var r = FfmpegLocator.Resolve(cfg, BesideDir, new[] { PathDir }, Existing(cfg, BesideExe, PathExe));

        Assert.Equal(cfg, r);
    }

    [Fact]
    public void Configured_folder_resolves_to_ffmpeg_exe_inside_it()
    {
        var cfgDir = Path.Combine("custom", "bin");
        var cfgExe = Path.Combine(cfgDir, "ffmpeg.exe");

        var r = FfmpegLocator.Resolve(cfgDir, BesideDir, new[] { PathDir }, Existing(cfgExe));

        Assert.Equal(cfgExe, r);
    }

    [Fact]
    public void Falls_back_to_beside_exe_when_unconfigured()
    {
        var r = FfmpegLocator.Resolve(null, BesideDir, new[] { PathDir }, Existing(BesideExe, PathExe));

        Assert.Equal(BesideExe, r);
    }

    [Fact]
    public void Falls_back_to_PATH_and_skips_empty_segments()
    {
        var r = FfmpegLocator.Resolve("", BesideDir, new[] { "", PathDir }, Existing(PathExe));

        Assert.Equal(PathExe, r);
    }

    [Fact]
    public void Quoted_PATH_entry_is_unquoted()
    {
        var dir = Path.Combine("opt", "ff");
        var exe = Path.Combine(dir, "ffmpeg.exe");

        var r = FfmpegLocator.Resolve(null, null, new[] { "\"" + dir + "\"" }, Existing(exe));

        Assert.Equal(exe, r);
    }

    [Fact]
    public void Returns_null_when_nowhere_to_be_found()
    {
        var r = FfmpegLocator.Resolve(null, BesideDir, new[] { PathDir }, Existing(/* nothing exists */));

        Assert.Null(r);
    }
}
