using System.IO;
using ClassLapse.Core;
using Xunit;

namespace ClassLapse.Tests;

public sealed class CaptureReliabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"classlapse-reliability-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort test cleanup */ }
    }

    [Fact]
    public async Task SaveAsync_same_timestamp_never_overwrites_and_leaves_no_temp_files()
    {
        var store = new CaptureFileStore();
        var timestamp = new DateTime(2026, 9, 1, 8, 30, 15, 123);
        byte[] jpeg = { 1, 2, 3, 4, 5 };

        var paths = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => store.SaveAsync(_root, timestamp, jpeg)));

        Assert.Equal(8, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(paths, path => Assert.Equal(jpeg, File.ReadAllBytes(path)));

        string dayDir = Path.Combine(_root, "2026-09-01");
        Assert.Empty(Directory.EnumerateFiles(dayDir, "*.tmp"));
        Assert.Equal(8, store.CountCapturesForDay(_root, new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task SaveAsync_preserves_an_existing_filename_and_uses_a_suffix()
    {
        var store = new CaptureFileStore();
        var timestamp = new DateTime(2026, 9, 1, 8, 30, 15, 123);
        string dayDir = Path.Combine(_root, "2026-09-01");
        Directory.CreateDirectory(dayDir);
        string existing = Path.Combine(dayDir, "08-30-15-123.jpg");
        byte[] original = { 9, 9, 9 };
        File.WriteAllBytes(existing, original);

        string saved = await store.SaveAsync(_root, timestamp, new byte[] { 1, 2, 3 });

        Assert.False(string.Equals(existing, saved, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(original, File.ReadAllBytes(existing));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(saved));
    }

    [Fact]
    public void Journal_finds_the_latest_success_and_keeps_failures()
    {
        string journalDir = Path.Combine(_root, "journal");
        var journal = new CaptureJournal(journalDir);
        var first = new DateTime(2026, 9, 1, 8, 0, 0);
        var failure = first.AddMinutes(1);
        var last = first.AddMinutes(2);

        Assert.True(journal.TryAppend(new CaptureJournal.Entry(
            first, true, @"D:\caps\first.jpg", 1920, 1080, 100, 300, null, null)));
        Assert.True(journal.TryAppend(new CaptureJournal.Entry(
            failure, false, null, 0, 0, 0, 4000, "Timeout", "camera busy")));
        Assert.True(journal.TryAppend(new CaptureJournal.Entry(
            last, true, @"D:\caps\last.jpg", 1920, 1080, 120, 320, null, null)));

        Assert.Equal(last, journal.FindLastSuccessfulCapture());
        Assert.Equal(3, File.ReadLines(Path.Combine(journalDir, "2026-09-01.jsonl")).Count());
    }

    [Fact]
    public void Journal_ignores_a_torn_line_when_restoring_last_success()
    {
        string journalDir = Path.Combine(_root, "journal");
        var journal = new CaptureJournal(journalDir);
        var timestamp = new DateTime(2026, 9, 1, 8, 0, 0);
        Assert.True(journal.TryAppend(new CaptureJournal.Entry(
            timestamp, true, @"D:\caps\frame.jpg", 1280, 720, 100, 200, null, null)));
        File.AppendAllText(Path.Combine(journalDir, "2026-09-01.jsonl"), "{torn\n");

        Assert.Equal(timestamp, journal.FindLastSuccessfulCapture());
    }

    [Fact]
    public void Watchdog_command_detection_does_not_capture_normal_cli_commands()
    {
        Assert.True(ProcessWatchdog.IsInvocation(new[] { "--watchdog", "123", "marker" }));
        Assert.False(ProcessWatchdog.IsInvocation(new[] { "--list-cameras" }));
        Assert.False(ProcessWatchdog.IsInvocation(Array.Empty<string>()));
    }

    [Fact]
    public async Task Watchdog_clean_exit_marker_prevents_restart_and_is_consumed()
    {
        string marker = Path.Combine(_root, "clean-exit.marker");
        Directory.CreateDirectory(_root);
        File.WriteAllText(marker, "clean");

        int exitCode = await ProcessWatchdog.RunAsync(new[]
        {
            ProcessWatchdog.Command,
            int.MaxValue.ToString(),
            marker,
        });

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(marker));
    }
}
