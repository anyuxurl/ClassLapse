using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;

namespace ClassLapse.Core;

/// <summary>
/// A lightweight companion process that restarts ClassLapse after an unexpected process exit.
/// Normal shutdown writes a stop marker first, so choosing Exit never relaunches the app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessWatchdog
{
    public const string Command = "--watchdog";
    private static readonly TimeSpan RestartCooldown = TimeSpan.FromSeconds(30);

    private readonly string _stopMarkerPath;
    private bool _cleanExitMarked;

    private ProcessWatchdog(string stopMarkerPath)
    {
        _stopMarkerPath = stopMarkerPath;
    }

    public static bool IsInvocation(string[] args)
        => args.Length > 0 && string.Equals(args[0], Command, StringComparison.Ordinal);

    public static ProcessWatchdog? TryStartForCurrentProcess()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return null;

            string watchdogDir = WatchdogDirectory();
            Directory.CreateDirectory(watchdogDir);
            DeleteOldMarkers(watchdogDir);

            string stopMarker = Path.Combine(
                watchdogDir,
                $"clean-exit-{Environment.ProcessId}-{Guid.NewGuid():N}.marker");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add(Command);
            psi.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(stopMarker);

            using var process = Process.Start(psi);
            if (process == null) return null;

            Log.Info($"watchdog started (PID {process.Id})");
            return new ProcessWatchdog(stopMarker);
        }
        catch (Exception ex)
        {
            Log.Warn("failed to start watchdog: " + ex.Message);
            return null;
        }
    }

    public void MarkCleanExit()
    {
        if (_cleanExitMarked) return;
        _cleanExitMarked = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stopMarkerPath)!);
            File.WriteAllText(_stopMarkerPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.Warn("failed to mark clean exit: " + ex.Message);
        }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3
            || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid)
            || string.IsNullOrWhiteSpace(args[2]))
        {
            return 2;
        }

        string stopMarker = args[2];
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            await parent.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The parent already exited before the watchdog finished starting.
        }
        catch (Exception ex)
        {
            Log.Warn("watchdog wait failed: " + ex.Message);
        }

        await Task.Delay(500).ConfigureAwait(false);
        if (File.Exists(stopMarker))
        {
            TryDelete(stopMarker);
            return 0;
        }

        if (!TryClaimRestartSlot())
        {
            Log.Error("watchdog suppressed restart because the app exited twice within 30 seconds");
            return 3;
        }

        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return 4;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            Log.Warn($"watchdog restarted ClassLapse after unexpected exit of PID {parentPid}");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("watchdog restart failed", ex);
            return 5;
        }
    }

    private static bool TryClaimRestartSlot()
    {
        try
        {
            string dir = WatchdogDirectory();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "last-restart.txt");
            var now = DateTime.UtcNow;

            if (File.Exists(path)
                && DateTime.TryParse(
                    File.ReadAllText(path),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var last)
                && now - last.ToUniversalTime() < RestartCooldown)
            {
                return false;
            }

            File.WriteAllText(path, now.ToString("O", CultureInfo.InvariantCulture));
            return true;
        }
        catch
        {
            // Failure to persist cooldown state should not prevent the first recovery attempt.
            return true;
        }
    }

    private static string WatchdogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassLapse",
        "watchdog");

    private static void DeleteOldMarkers(string dir)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(dir, "clean-exit-*.marker"))
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7)) TryDelete(path);
            }
        }
        catch { /* best-effort housekeeping */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort marker cleanup */ }
    }
}
