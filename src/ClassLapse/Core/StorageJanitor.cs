using System.Globalization;
using System.IO;
using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// Walks the {OutputFolder}/yyyy-MM-dd/ tree and prunes:
///   1. Day-directories older than <see cref="StorageConfig.AutoCleanupDays"/>.
///   2. Oldest day-directories until total size drops below
///      <see cref="StorageConfig.MaxDiskUsageGB"/>.
///
/// Today's directory is always kept. Failures on individual files are
/// swallowed — the next pass will retry. A self-imposed <see cref="MinInterval"/>
/// prevents this expensive scan from running every tick.
/// </summary>
public sealed class StorageJanitor
{
    private readonly Logger _logger;
    private readonly object _lock = new();
    private DateTime _lastRunAt = DateTime.MinValue;

    public StorageJanitor(Logger? logger = null)
    {
        _logger = logger ?? Log.Instance;
    }

    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Run a cleanup pass if <see cref="MinInterval"/> has elapsed since the last one.</summary>
    public void RunIfDue(StorageConfig config)
    {
        lock (_lock)
        {
            if (DateTime.Now - _lastRunAt < MinInterval) return;
            _lastRunAt = DateTime.Now;
        }
        Run(config);
    }

    /// <summary>Run a cleanup pass unconditionally. Safe to call concurrently; second call returns fast.</summary>
    public void Run(StorageConfig config)
    {
        if (!config.AutoCleanupEnabled) return;
        if (string.IsNullOrWhiteSpace(config.OutputFolder)) return;
        if (!Directory.Exists(config.OutputFolder)) return;

        try
        {
            int filesDeleted = 0;
            long bytesFreed = 0;

            var dayDirs = EnumerateDayDirectories(config.OutputFolder).ToList();
            var today = DateOnly.FromDateTime(DateTime.Today);

            // 1) Age-based pruning.
            if (config.AutoCleanupDays > 0)
            {
                var cutoff = today.AddDays(-config.AutoCleanupDays);
                foreach (var dd in dayDirs.ToArray())
                {
                    if (dd.Date < cutoff && dd.Date != today)
                    {
                        var (count, bytes) = DeleteDayDir(dd);
                        filesDeleted += count;
                        bytesFreed += bytes;
                        dayDirs.Remove(dd);
                    }
                }
            }

            // 2) Disk-cap pruning (oldest first, never delete today).
            if (config.MaxDiskUsageGB > 0)
            {
                long maxBytes = config.MaxDiskUsageGB * 1024L * 1024L * 1024L;
                long totalSize = dayDirs.Sum(d => d.SizeBytes);
                var ordered = dayDirs.OrderBy(d => d.Date).ToList();
                int i = 0;
                while (totalSize > maxBytes && i < ordered.Count)
                {
                    if (ordered[i].Date == today) { i++; continue; }
                    var (count, bytes) = DeleteDayDir(ordered[i]);
                    filesDeleted += count;
                    bytesFreed += bytes;
                    totalSize -= bytes;
                    i++;
                }
            }

            if (filesDeleted > 0)
            {
                _logger.Info($"clean: deleted {filesDeleted} files, freed {bytesFreed / 1024.0 / 1024.0:N1} MB");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("clean: pass failed", ex);
        }
    }

    private static IEnumerable<DayDirectory> EnumerateDayDirectories(string outputFolder)
    {
        foreach (var path in Directory.EnumerateDirectories(outputFolder))
        {
            var name = Path.GetFileName(path);
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var date))
            {
                yield return new DayDirectory(path, date, ComputeSize(path));
            }
        }
    }

    private static long ComputeSize(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        return total;
    }

    private static (int count, long bytes) DeleteDayDir(DayDirectory dd)
    {
        int count = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dd.Path, "*", SearchOption.AllDirectories))
            {
                count++;
            }
            Directory.Delete(dd.Path, recursive: true);
            return (count, dd.SizeBytes);
        }
        catch
        {
            return (0, 0);
        }
    }

    private sealed record DayDirectory(string Path, DateOnly Date, long SizeBytes);
}
