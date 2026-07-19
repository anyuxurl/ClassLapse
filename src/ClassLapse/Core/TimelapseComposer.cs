using System.Diagnostics;
using System.IO;
using System.Text;
using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// Runs ffmpeg to turn an ordered list of JPEG frames into an mp4. Orchestration only — the list
/// text and argv come from <see cref="FfmpegCommand"/>. Drains stdout (progress) and stderr (errors)
/// concurrently to avoid the pipe-buffer deadlock, reports per-frame progress, and always cleans up.
/// </summary>
public sealed class TimelapseComposer
{
    private readonly string _ffmpegPath;

    public TimelapseComposer(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public sealed record ComposeResult(bool Success, int ExitCode, string OutputPath, string? Error);

    /// <summary>True if the ffmpeg build advertises the given encoder (parses <c>-encoders</c> output).</summary>
    public async Task<bool> HasEncoderAsync(string encoder, CancellationToken ct = default)
    {
        try
        {
            var (exit, stdout, _) = await RunCaptureAsync(new[] { "-hide_banner", "-encoders" }, ct).ConfigureAwait(false);
            if (exit != 0) return false;

            foreach (var line in stdout.Split('\n'))
            {
                // Encoder rows look like " V....D libx264   H.264 ...": col 0 = flags, col 1 = name.
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == encoder) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True if the ffmpeg build includes the given video filter (parses <c>-filters</c> output).</summary>
    public async Task<bool> HasFilterAsync(string filter, CancellationToken ct = default)
    {
        try
        {
            var (exit, stdout, _) = await RunCaptureAsync(new[] { "-hide_banner", "-filters" }, ct).ConfigureAwait(false);
            if (exit != 0) return false;

            foreach (var line in stdout.Split('\n'))
            {
                // Filter rows look like " ... deflicker        V->V       Remove ...": flags, name, io, desc.
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == filter) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Compose <paramref name="orderedFrames"/> into <paramref name="outPath"/>. Progress reports
    /// 0..1 by frame. Cancellation kills ffmpeg and removes the partial output. The temp list file
    /// is always deleted.
    /// </summary>
    public async Task<ComposeResult> ComposeAsync(
        IReadOnlyList<string> orderedFrames,
        string outPath,
        TimelapseConfig cfg,
        bool hasLibx264,
        bool hasDeflicker,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (orderedFrames.Count == 0)
        {
            return new ComposeResult(false, -1, outPath, "没有可合成的帧");
        }

        int fps = cfg.Fps < 1 ? 1 : cfg.Fps;
        int total = orderedFrames.Count;
        string listPath = Path.Combine(Path.GetTempPath(), $"classlapse-concat-{Guid.NewGuid():N}.txt");
        string listText = FfmpegCommand.BuildList(orderedFrames, cfg.UseDurationListFallback, 1.0 / fps);

        try
        {
            await File.WriteAllTextAsync(listPath, listText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in FfmpegCommand.BuildArgs(listPath, outPath, cfg, hasLibx264, hasDeflicker))
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stderr = new StringBuilder();

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null || progress == null) return;
                if (e.Data.StartsWith("frame=", StringComparison.Ordinal)
                    && int.TryParse(e.Data.AsSpan(6).Trim(), out int frame))
                {
                    progress.Report(Math.Clamp(frame / (double)total, 0, 1));
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) stderr.AppendLine(e.Data);
            };

            if (!proc.Start())
            {
                return new ComposeResult(false, -1, outPath, "无法启动 ffmpeg");
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                TryDelete(outPath); // remove the partial mp4
                throw;
            }

            if (proc.ExitCode != 0)
            {
                Log.Warn($"timelapse: ffmpeg exited {proc.ExitCode}");
                TryDelete(outPath);
                return new ComposeResult(false, proc.ExitCode, outPath, Tail(stderr.ToString()));
            }

            progress?.Report(1);
            Log.Info($"timelapse: composed {total} frames -> {Path.GetFileName(outPath)}");
            return new ComposeResult(true, 0, outPath, null);
        }
        finally
        {
            TryDelete(listPath);
        }
    }

    // Run ffmpeg to completion capturing both streams (for short queries like -encoders).
    private async Task<(int exit, string stdout, string stderr)> RunCaptureAsync(
        IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
        try { proc.WaitForExit(3000); } catch { /* ignore */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private static string Tail(string s, int max = 600)
    {
        s = s.Trim();
        return s.Length <= max ? s : "…" + s[^max..];
    }
}
