using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClassLapse.Models;

namespace ClassLapse.Core;

[SupportedOSPlatform("windows")]
public static class DevCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        AttachOrAllocConsole();
        Console.WriteLine();

        try
        {
            return args[0] switch
            {
                "--list-cameras" => ListCameras(),
                "--capture" => await CaptureAsync(args),
                "--compose" => await ComposeAsync(args),
                "--help" or "-h" or "/?" => PrintHelp(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 99;
        }
    }

    private static int ListCameras()
    {
        var cams = CameraEnumerator.Enumerate();
        Console.WriteLine($"Found {cams.Count} camera(s):");
        Console.WriteLine();
        for (int i = 0; i < cams.Count; i++)
        {
            Console.WriteLine($"  [{i}] {cams[i].FriendlyName}");
            Console.WriteLine($"      moniker: {cams[i].MonikerString}");
            Console.WriteLine();
        }
        if (cams.Count == 0)
        {
            Console.WriteLine("  (no DirectShow video input devices detected)");
        }
        return 0;
    }

    private static async Task<int> CaptureAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: ClassLapse.exe --capture <camera-index> <output.jpg>");
            Console.Error.WriteLine("Tip: run --list-cameras first to find the index.");
            return 2;
        }

        var cams = CameraEnumerator.Enumerate();
        if (!int.TryParse(args[1], out int idx) || idx < 0 || idx >= cams.Count)
        {
            Console.Error.WriteLine($"Invalid camera index: {args[1]}. Found {cams.Count} camera(s) — valid range is 0..{cams.Count - 1}.");
            return 2;
        }

        var device = cams[idx];
        var outputPath = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        Console.WriteLine($"Capturing from [{idx}] {device.FriendlyName}");
        Console.WriteLine($"Output: {outputPath}");
        Console.WriteLine();

        var service = new CameraService();
        var sw = Stopwatch.StartNew();
        var result = await service.TryCaptureAsync(device.MonikerString, 0, 0, 90, useHighestResolution: true);
        sw.Stop();

        if (!result.Success)
        {
            Console.Error.WriteLine($"FAILED after {result.ElapsedMilliseconds}ms — {result.Failure}");
            Console.Error.WriteLine($"  {result.ErrorMessage}");
            return 3;
        }

        await File.WriteAllBytesAsync(outputPath, result.JpegBytes!);
        Console.WriteLine($"OK — {result.Width}x{result.Height} JPEG, {result.JpegBytes!.Length / 1024.0:N1} KB, {result.ElapsedMilliseconds}ms");
        return 0;
    }

    private static async Task<int> ComposeAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: ClassLapse.exe --compose <photos-folder> <output.mp4> [options]");
            Console.Error.WriteLine("Run --help for the full option list.");
            return 2;
        }

        string photosFolder = Path.GetFullPath(args[1]);
        string outPath = Path.GetFullPath(args[2]);

        var cfg = new TimelapseConfig(); // defaults: 30 fps, 1080p, crf 23, preset fast
        DateOnly? from = null, to = null;
        double? everyMinutes = null;

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--fps": if (!TryTakeInt(args, ref i, out int fps)) return 2; cfg.Fps = fps; break;
                case "--height": if (!TryTakeInt(args, ref i, out int h)) return 2; cfg.ResolutionHeight = h; break;
                case "--crf": if (!TryTakeInt(args, ref i, out int crf)) return 2; cfg.Crf = crf; break;
                case "--preset": if (!TryTake(args, ref i, out string preset)) return 2; cfg.Preset = preset; break;
                case "--ffmpeg": if (!TryTake(args, ref i, out string ff)) return 2; cfg.FfmpegPath = ff; break;
                case "--from": if (!TryTakeDate(args, ref i, out var f)) return 2; from = f; break;
                case "--to": if (!TryTakeDate(args, ref i, out var t)) return 2; to = t; break;
                case "--every": if (!TryTakeDouble(args, ref i, out double em)) return 2; everyMinutes = em; break;
                case "--no-deflicker": cfg.NormalizeBrightness = false; break;
                case "--duration-list": cfg.UseDurationListFallback = true; break;
                default:
                    Console.Error.WriteLine($"Unknown --compose option: {args[i]}");
                    return 2;
            }
        }

        if (cfg.Fps < 1) cfg.Fps = 1;

        if (!Directory.Exists(photosFolder))
        {
            Console.Error.WriteLine($"Photos folder not found: {photosFolder}");
            return 2;
        }

        var ffmpeg = FfmpegLocator.Find(cfg);
        if (ffmpeg == null)
        {
            Console.Error.WriteLine("ffmpeg not found. Pass --ffmpeg <path>, or put ffmpeg.exe beside the app / on PATH.");
            return 3;
        }

        var composer = new TimelapseComposer(ffmpeg);
        bool hasLibx264 = await composer.HasEncoderAsync(FfmpegCommand.Libx264);
        bool hasDeflicker = await composer.HasFilterAsync("deflicker");

        var days = CaptureLibrary.EnumerateDays(photosFolder)
            .Where(d => d.JpgCount > 0
                        && (from == null || d.Date >= from)
                        && (to == null || d.Date <= to))
            .ToList();
        if (days.Count == 0)
        {
            Console.Error.WriteLine("No day folders with photos matched the given range.");
            return 4;
        }

        var frames = CaptureLibrary.CollectFrames(days);
        int rawTotal = frames.Count;
        if (everyMinutes is > 0)
        {
            var cadence = TimeSpan.FromMinutes(everyMinutes.Value);
            frames = FrameResampler.Resample(CaptureLibrary.CollectFramesByDay(days), cadence);
        }
        int total = frames.Count;
        if (total == 0)
        {
            Console.Error.WriteLine("Matched days contained no usable frames.");
            return 4;
        }

        double estSeconds = total / (double)cfg.Fps;
        Console.WriteLine($"ffmpeg : {ffmpeg}");
        Console.WriteLine($"libx264: {(hasLibx264 ? "yes" : "no — mpeg4 fallback")}");
        Console.WriteLine($"deflicker: {(cfg.NormalizeBrightness ? (hasDeflicker ? "on (亮度统一)" : "requested but unavailable in this build — skipped") : "off")}");
        Console.WriteLine($"days   : {days.Count}  ({days[0].Date:yyyy-MM-dd} .. {days[^1].Date:yyyy-MM-dd})");
        if (everyMinutes is > 0)
        {
            Console.WriteLine($"resample: 1 frame / {everyMinutes.Value:0.###} real-min  ->  {rawTotal:N0} frames thinned to {total:N0}");
        }
        Console.WriteLine($"frames : {total:N0}  ->  ~{estSeconds:N1}s @ {cfg.Fps}fps  (height={cfg.ResolutionHeight}, crf={cfg.Crf}, preset={cfg.Preset})");
        Console.WriteLine($"output : {outPath}");
        Console.WriteLine();

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var sw = Stopwatch.StartNew();
        int lastPct = -1;
        var progress = new Progress<double>(p =>
        {
            int pct = (int)(p * 100);
            if (pct == lastPct) return;
            lastPct = pct;
            Console.Write($"\rcomposing… {pct,3}%  ({(int)(p * total):N0}/{total:N0})   ");
        });

        var result = await composer.ComposeAsync(frames, outPath, cfg, hasLibx264, hasDeflicker, progress);
        sw.Stop();
        Console.WriteLine();

        if (!result.Success)
        {
            Console.Error.WriteLine($"FAILED (exit {result.ExitCode}): {result.Error}");
            return 5;
        }

        long size = new FileInfo(outPath).Length;
        Console.WriteLine($"OK — {total:N0} frames -> {outPath}");
        Console.WriteLine($"     {size / 1024.0 / 1024.0:N1} MB encoded in {sw.Elapsed.TotalSeconds:N1}s");
        return 0;
    }

    // ----- --compose arg helpers -----

    private static bool TryTake(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Missing value after {args[i]}");
            value = "";
            return false;
        }
        value = args[++i];
        return true;
    }

    private static bool TryTakeInt(string[] args, ref int i, out int value)
    {
        value = 0;
        if (!TryTake(args, ref i, out string s)) return false;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            Console.Error.WriteLine($"Expected a number after {args[i - 1]}, got '{s}'");
            return false;
        }
        return true;
    }

    private static bool TryTakeDouble(string[] args, ref int i, out double value)
    {
        value = 0;
        if (!TryTake(args, ref i, out string s)) return false;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            Console.Error.WriteLine($"Expected a number after {args[i - 1]}, got '{s}'");
            return false;
        }
        return true;
    }

    private static bool TryTakeDate(string[] args, ref int i, out DateOnly value)
    {
        value = default;
        if (!TryTake(args, ref i, out string s)) return false;
        if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            Console.Error.WriteLine($"Expected yyyy-MM-dd after {args[i - 1]}, got '{s}'");
            return false;
        }
        return true;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("ClassLapse dev CLI (M1)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ClassLapse.exe --list-cameras           List all DirectShow video input devices");
        Console.WriteLine("  ClassLapse.exe --capture <idx> <out>    Capture one 1280x720 JPEG from camera at index");
        Console.WriteLine("  ClassLapse.exe --compose <src> <out>    Compose a timelapse mp4 from a photos folder");
        Console.WriteLine("  ClassLapse.exe --help                   This message");
        Console.WriteLine();
        Console.WriteLine("--compose <photos-folder> <output.mp4> [options]");
        Console.WriteLine("  Drives the real compose path (CaptureLibrary -> FfmpegCommand -> TimelapseComposer)");
        Console.WriteLine("  over a folder of yyyy-MM-dd/*.jpg day folders. A dev harness for iterating on the");
        Console.WriteLine("  video pipeline without the GUI.");
        Console.WriteLine("  Options:");
        Console.WriteLine("    --fps N            output frames per second (default 30)");
        Console.WriteLine("    --height N         target height, width auto-even; 0 = original (default 1080)");
        Console.WriteLine("    --crf N            libx264 quality, lower = better (default 23)");
        Console.WriteLine("    --preset NAME      libx264 preset (default fast)");
        Console.WriteLine("    --every MIN        timestamp resample: keep ~1 frame per MIN real minutes");
        Console.WriteLine("                       (thins dense days, leaves sparse days as-is; 真实等速)");
        Console.WriteLine("    --no-deflicker     turn off brightness unification (亮度统一; on by default)");
        Console.WriteLine("    --from yyyy-MM-dd  include days on/after this date");
        Console.WriteLine("    --to   yyyy-MM-dd  include days on/before this date");
        Console.WriteLine("    --ffmpeg PATH      ffmpeg.exe (or its folder); else auto-detect beside app / on PATH");
        Console.WriteLine("    --duration-list    use the per-file duration list form (for builds that ignore -r)");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - From PowerShell, pipe to Out-Default if no output appears:");
        Console.WriteLine("      .\\ClassLapse.exe --list-cameras | Out-Default");
        Console.WriteLine("  - Or use dotnet run during dev:");
        Console.WriteLine("      dotnet run --project src\\ClassLapse -- --list-cameras");
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        Console.Error.WriteLine();
        PrintHelp();
        return 2;
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private static void AttachOrAllocConsole()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
        {
            AllocConsole();
        }
    }
}
