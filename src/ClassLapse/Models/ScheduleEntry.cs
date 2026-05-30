namespace ClassLapse.Models;

/// <summary>
/// One independently-configured slice of the capture schedule. An entry is either an
/// <see cref="ScheduleMode.Interval"/> (snap every N seconds inside a window) or a
/// <see cref="ScheduleMode.SpecificTimes"/> (snap once at each listed clock time).
/// </summary>
/// <remarks>
/// <see cref="Id"/> is a stable per-entry key: the scheduler tracks each entry's last
/// capture under it, so it must survive config round-trips and edits. Brand-new entries
/// mint a fresh GUID; migrated legacy windows get a deterministic <c>legacy-{i}</c>.
/// </remarks>
public sealed class ScheduleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    /// <summary>Optional human label shown in the settings UI (not used by the scheduler).</summary>
    public string Name { get; set; } = "";

    public ScheduleMode Mode { get; set; } = ScheduleMode.Interval;

    public DayOfWeek[] ActiveDays { get; set; } = new[]
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
    };

    // ----- Interval mode -----
    public TimeWindow Window { get; set; } = new(new TimeOnly(8, 0), new TimeOnly(11, 30));
    public int IntervalSeconds { get; set; } = 30;

    // ----- SpecificTimes mode -----
    public TimeOnly[] Times { get; set; } = Array.Empty<TimeOnly>();
}
