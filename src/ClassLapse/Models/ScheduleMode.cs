namespace ClassLapse.Models;

public enum ScheduleMode
{
    /// <summary>Capture every <c>IntervalSeconds</c> while the clock is inside <c>Window</c>.</summary>
    Interval,

    /// <summary>Capture exactly once at each clock time listed in <c>Times</c>.</summary>
    SpecificTimes,
}
