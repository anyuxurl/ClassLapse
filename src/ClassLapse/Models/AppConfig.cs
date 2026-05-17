namespace ClassLapse.Models;

public sealed class AppConfig
{
    public ScheduleConfig Schedule { get; set; } = new();
    public CameraConfig Camera { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public bool AutoStartWithWindows { get; set; } = true;
    public DateTime? PausedUntil { get; set; }
}
