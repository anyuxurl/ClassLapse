using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

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
        var result = await service.TryCaptureAsync(device.MonikerString, 1280, 720, 85);
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

    private static int PrintHelp()
    {
        Console.WriteLine("ClassLapse dev CLI (M1)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ClassLapse.exe --list-cameras           List all DirectShow video input devices");
        Console.WriteLine("  ClassLapse.exe --capture <idx> <out>    Capture one 1280x720 JPEG from camera at index");
        Console.WriteLine("  ClassLapse.exe --help                   This message");
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
