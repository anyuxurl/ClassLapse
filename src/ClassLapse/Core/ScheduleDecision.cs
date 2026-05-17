using ClassLapse.Models;

namespace ClassLapse.Core;

public static class ScheduleDecision
{
    public enum Reason
    {
        ShouldCapture,
        OutsideActiveDay,
        BeforeWindow,
        AfterWindow,
        TooSoon,
        Paused,
    }

    /// <summary>
    /// Pure decision: given a clock reading, last capture time, schedule, and pause state,
    /// determine whether a capture should happen now (and if not, why not).
    /// </summary>
    /// <remarks>
    /// The schedule window is [StartTime, EndTime) — start inclusive, end exclusive.
    /// Pause takes priority over every other reason: an explicit user pause means
    /// "stop, regardless of schedule".
    /// </remarks>
    public static Reason Evaluate(
        DateTime now,
        DateTime? lastCaptureAt,
        ScheduleConfig schedule,
        DateTime? pausedUntil)
    {
        if (pausedUntil is { } until && until > now)
        {
            return Reason.Paused;
        }

        if (Array.IndexOf(schedule.ActiveDays, now.DayOfWeek) < 0)
        {
            return Reason.OutsideActiveDay;
        }

        var nowTime = TimeOnly.FromDateTime(now);
        if (nowTime < schedule.StartTime)
        {
            return Reason.BeforeWindow;
        }
        if (nowTime >= schedule.EndTime)
        {
            return Reason.AfterWindow;
        }

        if (lastCaptureAt is { } last)
        {
            double elapsedSeconds = (now - last).TotalSeconds;
            if (elapsedSeconds < schedule.IntervalSeconds)
            {
                return Reason.TooSoon;
            }
        }

        return Reason.ShouldCapture;
    }
}
