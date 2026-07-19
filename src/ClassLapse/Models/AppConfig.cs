namespace ClassLapse.Models;

public sealed class AppConfig
{
    /// <summary>Bumped whenever the on-disk shape changes; see <see cref="Core.LegacyScheduleMigration"/>.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ScheduleConfig Schedule { get; set; } = new();
    public CameraConfig Camera { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public TimelapseConfig Timelapse { get; set; } = new();
    public WatermarkConfig Watermark { get; set; } = new();
    public bool AutoStartWithWindows { get; set; } = true;

    /// <summary>Timed pause: capture is suspended until this instant, then auto-resumes. Null/past = not timed-paused.</summary>
    public DateTime? PausedUntil { get; set; }

    /// <summary>
    /// Open-ended "vacation" pause: capture stays suspended until the user manually resumes,
    /// regardless of <see cref="PausedUntil"/>. Persisted like any config, so it survives restarts —
    /// a classroom PC that reboots over a holiday stays paused. Takes priority over
    /// <see cref="PausedUntil"/> in <see cref="Core.ScheduleDecision.Evaluate"/>.
    /// </summary>
    public bool PausedIndefinitely { get; set; }
}
