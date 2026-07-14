using System.Globalization;
using System.Text;
using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// Pure builders for the ffmpeg invocation — the ffconcat list text and the argv. No I/O and no
/// process launch, so the exact strings handed to ffmpeg are unit-testable.
/// </summary>
public static class FfmpegCommand
{
    public const string Libx264 = "libx264";
    public const string Mpeg4Fallback = "mpeg4";

    /// <summary>
    /// One ffconcat <c>file</c> directive. Backslashes become forward slashes (sidesteps ffmpeg's
    /// backslash escaping), the path is single-quoted, and embedded quotes are escaped as <c>'\''</c>.
    /// </summary>
    public static string ToConcatLine(string absPath)
    {
        var p = absPath.Replace('\\', '/').Replace("'", "'\\''");
        return "file '" + p + "'";
    }

    /// <summary>
    /// Build the ffconcat list text (write it as UTF-8 without BOM — a BOM breaks the header).
    /// <para><paramref name="useDurations"/> false (primary, paired with input <c>-r</c>): one bare
    /// <c>file</c> line per frame.</para>
    /// <para>true (fallback for builds that ignore input <c>-r</c>): a <c>duration</c> after each file
    /// plus the last file repeated so its final frame is not dropped.</para>
    /// </summary>
    public static string BuildList(IReadOnlyList<string> orderedPaths, bool useDurations, double secondsPerFrame)
    {
        var sb = new StringBuilder();
        sb.Append("ffconcat version 1.0\n");
        foreach (var path in orderedPaths)
        {
            sb.Append(ToConcatLine(path)).Append('\n');
            if (useDurations)
            {
                sb.Append("duration ")
                  .Append(secondsPerFrame.ToString("0.######", CultureInfo.InvariantCulture))
                  .Append('\n');
            }
        }
        if (useDurations && orderedPaths.Count > 0)
        {
            sb.Append(ToConcatLine(orderedPaths[^1])).Append('\n'); // repeat last so its frame shows
        }
        return sb.ToString();
    }

    /// <summary>
    /// The ffmpeg argv (one token per element — pass via <c>ProcessStartInfo.ArgumentList</c> so
    /// Windows quoting is handled). Encoder is libx264 (crf) when available, else an mpeg4 fallback.
    /// </summary>
    public static IReadOnlyList<string> BuildArgs(string listPath, string outPath, TimelapseConfig cfg, bool hasLibx264)
    {
        int fps = cfg.Fps < 1 ? 1 : cfg.Fps;
        string fpsText = fps.ToString(CultureInfo.InvariantCulture);

        var args = new List<string> { "-y", "-hide_banner", "-nostats", "-progress", "pipe:1" };

        // Primary: input -r before -i makes the concat image input exactly one-frame-per-image.
        // Fallback: omit it here; the per-file durations define timing and -r is pinned on output.
        if (!cfg.UseDurationListFallback)
        {
            args.Add("-r");
            args.Add(fpsText);
        }

        args.Add("-f"); args.Add("concat");
        args.Add("-safe"); args.Add("0");
        args.Add("-i"); args.Add(listPath);

        args.Add("-vf"); args.Add(BuildScaleFilter(cfg.ResolutionHeight));
        args.Add("-fps_mode"); args.Add("cfr");

        if (cfg.UseDurationListFallback)
        {
            args.Add("-r");
            args.Add(fpsText);
        }

        if (hasLibx264)
        {
            args.Add("-c:v"); args.Add(Libx264);
            args.Add("-crf"); args.Add((cfg.Crf < 0 ? 23 : cfg.Crf).ToString(CultureInfo.InvariantCulture));
            args.Add("-preset"); args.Add(string.IsNullOrWhiteSpace(cfg.Preset) ? "fast" : cfg.Preset);
        }
        else
        {
            args.Add("-c:v"); args.Add(Mpeg4Fallback);
            args.Add("-q:v"); args.Add("5");
        }

        args.Add("-movflags"); args.Add("+faststart");
        args.Add(outPath);
        return args;
    }

    /// <summary>height &gt; 0: scale to that height with even width. 0: keep original size, dims forced even. Always yuv420p.</summary>
    public static string BuildScaleFilter(int height)
        => height > 0
            ? $"scale=-2:{height},format=yuv420p"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2,format=yuv420p";
}
