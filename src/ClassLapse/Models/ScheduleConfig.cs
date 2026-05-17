namespace ClassLapse.Models;

public sealed class ScheduleConfig
{
    public DayOfWeek[] ActiveDays { get; set; } = new[]
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
    };

    public TimeWindow[] TimeWindows { get; set; } = new[]
    {
        new TimeWindow(new TimeOnly(8, 0), new TimeOnly(11, 30)),
        new TimeWindow(new TimeOnly(13, 30), new TimeOnly(17, 0)),
    };

    public int IntervalSeconds { get; set; } = 30;
}
