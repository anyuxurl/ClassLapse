using System.IO;
using ClassLapse.Core;
using ClassLapse.Models;
using Xunit;

namespace ClassLapse.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tmpPath;

    public ConfigStoreTests()
    {
        _tmpPath = Path.Combine(Path.GetTempPath(), $"classlapse-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_tmpPath)!;
        var name = Path.GetFileName(_tmpPath);
        if (File.Exists(_tmpPath)) File.Delete(_tmpPath);
        foreach (var bak in Directory.GetFiles(dir, $"{name}.bak.*"))
        {
            try { File.Delete(bak); } catch { }
        }
    }

    // Legacy (v1) on-disk shape: one global ActiveDays + TimeWindows + IntervalSeconds, no Entries.
    private const string LegacyJson =
        @"{
            ""Schedule"": {
                ""ActiveDays"": [""Monday"", ""Wednesday"", ""Friday""],
                ""TimeWindows"": [
                    { ""Start"": ""08:00:00"", ""End"": ""11:30:00"" },
                    { ""Start"": ""13:30:00"", ""End"": ""17:00:00"" }
                ],
                ""IntervalSeconds"": 45
            },
            ""Camera"": { ""JpegQuality"": 70 },
            ""AutoStartWithWindows"": false
        }";

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new ConfigStore(_tmpPath);

        var config = store.Load();

        Assert.Equal(AppConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal(2, config.Schedule.Entries.Length);
        Assert.All(config.Schedule.Entries, e => Assert.Equal(ScheduleMode.Interval, e.Mode));
        Assert.Equal(30, config.Schedule.Entries[0].IntervalSeconds);
        Assert.Equal(new TimeOnly(8, 0), config.Schedule.Entries[0].Window.Start);
        Assert.Equal(new TimeOnly(11, 30), config.Schedule.Entries[0].Window.End);
        Assert.Equal(new TimeOnly(13, 30), config.Schedule.Entries[1].Window.Start);
        Assert.Equal(new TimeOnly(17, 0), config.Schedule.Entries[1].Window.End);
        Assert.True(config.Camera.UseHighestResolution);
        Assert.True(config.AutoStartWithWindows);
        Assert.Equal(85, config.Camera.JpegQuality);
    }

    [Fact]
    public void Save_then_Load_preserves_all_fields()
    {
        var store = new ConfigStore(_tmpPath);
        var written = new AppConfig
        {
            Schedule = new ScheduleConfig
            {
                Entries = new[]
                {
                    new ScheduleEntry
                    {
                        Id = "weekend-am",
                        Name = "周末上午",
                        Mode = ScheduleMode.Interval,
                        ActiveDays = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                        Window = new TimeWindow(new TimeOnly(9, 30), new TimeOnly(12, 0)),
                        IntervalSeconds = 15,
                    },
                    new ScheduleEntry
                    {
                        Id = "bell-times",
                        Name = "打铃",
                        Mode = ScheduleMode.SpecificTimes,
                        Enabled = false,
                        ActiveDays = new[] { DayOfWeek.Monday },
                        Times = new[] { new TimeOnly(8, 0), new TimeOnly(9, 55), new TimeOnly(14, 0) },
                    },
                },
            },
            Camera = new CameraConfig
            {
                DeviceMoniker = "@device:pnp:test",
                FriendlyName = "Test Cam",
                UseHighestResolution = false,
                Width = 640,
                Height = 480,
                JpegQuality = 70,
            },
            Storage = new StorageConfig
            {
                OutputFolder = @"D:\out",
                AutoCleanupEnabled = true,
                AutoCleanupDays = 14,
                MaxDiskUsageGB = 20,
            },
            AutoStartWithWindows = false,
            PausedUntil = new DateTime(2026, 5, 17, 14, 0, 0),
        };

        store.Save(written);
        var read = store.Load();

        Assert.Equal(2, read.Schedule.Entries.Length);

        var am = read.Schedule.Entries[0];
        Assert.Equal("weekend-am", am.Id);
        Assert.Equal(ScheduleMode.Interval, am.Mode);
        Assert.Equal(15, am.IntervalSeconds);
        Assert.Equal(new TimeOnly(9, 30), am.Window.Start);
        Assert.Equal(new TimeOnly(12, 0), am.Window.End);
        Assert.Equal(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, am.ActiveDays);

        var bell = read.Schedule.Entries[1];
        Assert.Equal(ScheduleMode.SpecificTimes, bell.Mode);
        Assert.False(bell.Enabled);
        Assert.Equal(new[] { new TimeOnly(8, 0), new TimeOnly(9, 55), new TimeOnly(14, 0) }, bell.Times);

        Assert.Equal("Test Cam", read.Camera.FriendlyName);
        Assert.False(read.Camera.UseHighestResolution);
        Assert.Equal(640, read.Camera.Width);
        Assert.Equal(@"D:\out", read.Storage.OutputFolder);
        Assert.True(read.Storage.AutoCleanupEnabled);
        Assert.Equal(20, read.Storage.MaxDiskUsageGB);
        Assert.False(read.AutoStartWithWindows);
        Assert.Equal(new DateTime(2026, 5, 17, 14, 0, 0), read.PausedUntil);
    }

    [Fact]
    public void Load_migrates_legacy_schedule_to_entries()
    {
        File.WriteAllText(_tmpPath, LegacyJson);
        var store = new ConfigStore(_tmpPath);

        var config = store.Load();

        Assert.Equal(2, config.Schedule.Entries.Length);

        var e0 = config.Schedule.Entries[0];
        Assert.Equal("legacy-0", e0.Id);
        Assert.Equal(ScheduleMode.Interval, e0.Mode);
        Assert.Equal(45, e0.IntervalSeconds);
        Assert.Equal(new TimeOnly(8, 0), e0.Window.Start);
        Assert.Equal(new TimeOnly(11, 30), e0.Window.End);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, e0.ActiveDays);

        Assert.Equal("legacy-1", config.Schedule.Entries[1].Id);
        Assert.Equal(new TimeOnly(13, 30), config.Schedule.Entries[1].Window.Start);
        Assert.Equal(45, config.Schedule.Entries[1].IntervalSeconds);

        // Non-schedule fields survive migration.
        Assert.Equal(70, config.Camera.JpegQuality);
        Assert.False(config.AutoStartWithWindows);
    }

    [Fact]
    public void MigrateOnDiskIfNeeded_upgrades_file_and_is_idempotent()
    {
        File.WriteAllText(_tmpPath, LegacyJson);
        var store = new ConfigStore(_tmpPath);

        store.MigrateOnDiskIfNeeded();

        string upgraded = File.ReadAllText(_tmpPath);
        Assert.Contains("\"Entries\"", upgraded);
        Assert.DoesNotContain("TimeWindows", upgraded);

        // Deterministic ids → repeated loads and a repeated on-disk migration never churn ids.
        var first = store.Load();
        store.MigrateOnDiskIfNeeded(); // second pass is a no-op
        var second = store.Load();

        Assert.Equal("legacy-0", first.Schedule.Entries[0].Id);
        Assert.Equal(first.Schedule.Entries[0].Id, second.Schedule.Entries[0].Id);
        Assert.Equal(45, second.Schedule.Entries[0].IntervalSeconds);
    }

    [Fact]
    public void Load_with_corrupted_json_backs_up_and_returns_defaults()
    {
        File.WriteAllText(_tmpPath, "{ this is not valid json");
        var store = new ConfigStore(_tmpPath);

        var config = store.Load();

        Assert.Equal(2, config.Schedule.Entries.Length);
        Assert.False(File.Exists(_tmpPath));

        var dir = Path.GetDirectoryName(_tmpPath)!;
        var name = Path.GetFileName(_tmpPath);
        var backups = Directory.GetFiles(dir, $"{name}.bak.*");
        Assert.Single(backups);
    }

    [Fact]
    public void Save_creates_parent_directory_when_missing()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"classlapse-test-{Guid.NewGuid():N}");
        var nestedPath = Path.Combine(subDir, "config.json");
        try
        {
            var store = new ConfigStore(nestedPath);
            store.Save(new AppConfig());

            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            if (Directory.Exists(subDir)) Directory.Delete(subDir, recursive: true);
        }
    }

    [Fact]
    public void Save_is_atomic_via_temp_then_rename()
    {
        var store = new ConfigStore(_tmpPath);
        store.Save(ConfigWithInterval(60));
        store.Save(ConfigWithInterval(90));

        // No leftover .tmp file
        Assert.False(File.Exists(_tmpPath + ".tmp"));
        Assert.Equal(90, store.Load().Schedule.Entries[0].IntervalSeconds);
    }

    [Fact]
    public void Load_defaults_enable_watermark()
    {
        var store = new ConfigStore(_tmpPath);

        var config = store.Load();

        Assert.True(config.Watermark.Enabled);
        Assert.Equal(WatermarkPosition.BottomRight, config.Watermark.Position);
        Assert.Equal("yyyy-MM-dd HH:mm:ss", config.Watermark.Format);
    }

    [Fact]
    public void Save_then_Load_preserves_watermark()
    {
        var store = new ConfigStore(_tmpPath);
        store.Save(new AppConfig
        {
            Watermark = new WatermarkConfig
            {
                Enabled = false,
                Position = WatermarkPosition.TopLeft,
                Format = "HH:mm",
                FontSize = 42,
                Color = "#00FF00",
                Outline = false,
            },
        });

        var read = store.Load();

        Assert.False(read.Watermark.Enabled);
        Assert.Equal(WatermarkPosition.TopLeft, read.Watermark.Position);
        Assert.Equal("HH:mm", read.Watermark.Format);
        Assert.Equal(42, read.Watermark.FontSize);
        Assert.Equal("#00FF00", read.Watermark.Color);
        Assert.False(read.Watermark.Outline);
    }

    private static AppConfig ConfigWithInterval(int seconds) => new()
    {
        Schedule = new ScheduleConfig
        {
            Entries = new[]
            {
                new ScheduleEntry { Id = "e", Mode = ScheduleMode.Interval, IntervalSeconds = seconds },
            },
        },
    };
}
