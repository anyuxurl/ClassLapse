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

    public TimeOnly StartTime { get; set; } = new(8, 0);

    public TimeOnly EndTime { get; set; } = new(17, 0);

    public int IntervalSeconds { get; set; } = 30;
}
