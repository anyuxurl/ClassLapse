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

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new ConfigStore(_tmpPath);

        var config = store.Load();

        Assert.Equal(30, config.Schedule.IntervalSeconds);
        Assert.Equal(new TimeOnly(8, 0), config.Schedule.StartTime);
        Assert.Equal(new TimeOnly(17, 0), config.Schedule.EndTime);
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
                ActiveDays = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                StartTime = new TimeOnly(9, 30),
                EndTime = new TimeOnly(16, 45),
                IntervalSeconds = 15,
            },
            Camera = new CameraConfig
            {
                DeviceMoniker = "@device:pnp:test",
                FriendlyName = "Test Cam",
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

        Assert.Equal(15, read.Schedule.IntervalSeconds);
        Assert.Equal(new TimeOnly(9, 30), read.Schedule.StartTime);
        Assert.Equal(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, read.Schedule.ActiveDays);
        Assert.Equal("Test Cam", read.Camera.FriendlyName);
        Assert.Equal(640, read.Camera.Width);
        Assert.Equal(@"D:\out", read.Storage.OutputFolder);
        Assert.True(read.Storage.AutoCleanupEnabled);
        Assert.Equal(20, read.Storage.MaxDiskUsageGB);
        Assert.False(read.AutoStartWithWindows);
        Assert.Equal(new DateTime(2026, 5, 17, 14, 0, 0), read.PausedUntil);
    }

    [Fact]
    public void Load_with_corrupted_json_backs_up_and_returns_defaults()
    {
        File.WriteAllText(_tmpPath, "{ this is not valid json");
        var store = new ConfigStore(_tmpPath);

        var config = store.Load();

        Assert.Equal(30, config.Schedule.IntervalSeconds);
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
        store.Save(new AppConfig { Schedule = new ScheduleConfig { IntervalSeconds = 60 } });
        store.Save(new AppConfig { Schedule = new ScheduleConfig { IntervalSeconds = 90 } });

        // No leftover .tmp file
        Assert.False(File.Exists(_tmpPath + ".tmp"));
        Assert.Equal(90, store.Load().Schedule.IntervalSeconds);
    }
}
