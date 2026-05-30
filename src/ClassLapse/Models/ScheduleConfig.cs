namespace ClassLapse.Models;

public sealed class ScheduleConfig
{
    /// <summary>
    /// The schedule is a list of independent entries. Default mirrors the original
    /// hard-coded schedule: morning + afternoon interval blocks, weekdays, 30s.
    /// </summary>
    public ScheduleEntry[] Entries { get; set; } =
    {
        new()
        {
            Id = "default-morning",
            Name = "上午",
            Mode = ScheduleMode.Interval,
            Window = new TimeWindow(new TimeOnly(8, 0), new TimeOnly(11, 30)),
            IntervalSeconds = 30,
        },
        new()
        {
            Id = "default-afternoon",
            Name = "下午",
            Mode = ScheduleMode.Interval,
            Window = new TimeWindow(new TimeOnly(13, 30), new TimeOnly(17, 0)),
            IntervalSeconds = 30,
        },
    };
}
