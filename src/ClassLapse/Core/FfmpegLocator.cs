using System.IO;
using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// Finds the ffmpeg executable: a configured path (the .exe, or a folder containing it), then
/// <c>ffmpeg.exe</c> next to the app exe, then each directory on PATH. The pure <see cref="Resolve"/>
/// core takes an existence predicate so it is unit-testable without touching the filesystem.
/// </summary>
public static class FfmpegLocator
{
    private const string ExeName = "ffmpeg.exe";

    /// <summary>Resolve against the real environment; null if ffmpeg can't be found anywhere.</summary>
    public static string? Find(TimelapseConfig config)
        => Resolve(config.FfmpegPath, AppExeDirectory(), PathDirectories(), File.Exists);

    /// <summary>
    /// Pure resolution: configured (a file, or a folder holding ffmpeg.exe) → besideExeDir/ffmpeg.exe
    /// → each pathDir/ffmpeg.exe. Returns the first existing full path, or null.
    /// </summary>
    public static string? Resolve(
        string? configuredPath,
        string? besideExeDir,
        IEnumerable<string> pathDirs,
        Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var c = configuredPath.Trim();
            if (exists(c)) return c;                       // configured points straight at the exe
            var inDir = Path.Combine(c, ExeName);
            if (exists(inDir)) return inDir;               // configured points at a folder
        }

        if (!string.IsNullOrWhiteSpace(besideExeDir))
        {
            var beside = Path.Combine(besideExeDir, ExeName);
            if (exists(beside)) return beside;
        }

        foreach (var dir in pathDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim().Trim('"'), ExeName);
            if (exists(candidate)) return candidate;
        }

        return null;
    }

    private static string? AppExeDirectory()
    {
        // Environment.ProcessPath is the real exe even under single-file publish, where
        // AppContext.BaseDirectory would point at the extraction temp dir.
        var exe = Environment.ProcessPath;
        return string.IsNullOrEmpty(exe) ? AppContext.BaseDirectory : Path.GetDirectoryName(exe);
    }

    private static IEnumerable<string> PathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return Array.Empty<string>();
        return path.Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
