using ClassLapse.Models;

namespace ClassLapse.Core;

public static class ScheduleDecision
{
    public enum Reason
    {
        ShouldCapture,
        OutsideActiveDay,
        OutsideTimeWindows,
        TooSoon,
        Paused,
    }

    /// <summary>
    /// Pure decision: given a clock reading, last capture time, schedule, and pause state,
    /// determine whether a capture should happen now (and if not, why not).
    /// </summary>
    /// <remarks>
    /// Each window is [Start, End) — start inclusive, end exclusive. The reading must
    /// fall in at least one window. Pause takes priority over every other reason.
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
        bool inAnyWindow = false;
        foreach (var w in schedule.TimeWindows)
        {
            if (w.Contains(nowTime))
            {
                inAnyWindow = true;
                break;
            }
        }
        if (!inAnyWindow)
        {
            return Reason.OutsideTimeWindows;
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
