namespace ClassLapse.Models;

public sealed class StorageConfig
{
    public string OutputFolder { get; set; } = "";

    public bool AutoCleanupEnabled { get; set; } = false;

    public int AutoCleanupDays { get; set; } = 30;

    public long MaxDiskUsageGB { get; set; } = 0;
}
