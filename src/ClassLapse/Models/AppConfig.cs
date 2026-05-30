namespace ClassLapse.Models;

public sealed class AppConfig
{
    /// <summary>Bumped whenever the on-disk shape changes; see <see cref="Core.LegacyScheduleMigration"/>.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ScheduleConfig Schedule { get; set; } = new();
    public CameraConfig Camera { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public bool AutoStartWithWindows { get; set; } = true;
    public DateTime? PausedUntil { get; set; }
}
