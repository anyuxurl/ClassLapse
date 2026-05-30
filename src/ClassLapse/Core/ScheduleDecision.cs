using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// The aggregate result of one schedule evaluation: a single <see cref="ScheduleDecision.Reason"/>
/// for the tray UI, plus the ids of every entry that wants a capture right now
/// (non-empty only when <see cref="Reason"/> is <see cref="ScheduleDecision.Reason.ShouldCapture"/>).
/// </summary>
public sealed record ScheduleEvaluation(ScheduleDecision.Reason Reason, string[] DueEntryIds);

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
    /// A SpecificTimes point fires once when a tick lands in <c>[P, P + tolerance)</c>.
    /// 60s == the resolution of the HH:mm input, so this is "fire during the scheduled minute".
    /// </summary>
    public static readonly TimeSpan SpecificTimeTolerance = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Pure decision over the whole entry list. Each enabled entry is judged independently
    /// against its own last-capture (looked up by <see cref="ScheduleEntry.Id"/>); the result
    /// reports which entries are due and a single aggregate reason for the tray.
    /// </summary>
    /// <remarks>
    /// Aggregate priority (most "active" wins, so the tray never says "waiting" while an entry
    /// is capturing): Paused &gt; ShouldCapture &gt; TooSoon &gt; OutsideTimeWindows &gt; OutsideActiveDay.
    /// Pause is global and beats everything.
    /// </remarks>
    public static ScheduleEvaluation Evaluate(
        DateTime now,
        ScheduleConfig schedule,
        DateTime? pausedUntil,
        IReadOnlyDictionary<string, DateTime> lastByEntryId)
    {
        if (pausedUntil is { } until && until > now)
        {
            return new ScheduleEvaluation(Reason.Paused, Array.Empty<string>());
        }

        var due = new List<string>();
        bool anyActiveToday = false;       // some enabled entry runs on this weekday
        bool anyInWindowTooSoon = false;   // an interval entry is in its window but interval not elapsed

        foreach (var entry in schedule.Entries)
        {
            if (!entry.Enabled) continue;
            if (Array.IndexOf(entry.ActiveDays, now.DayOfWeek) < 0) continue;
            anyActiveToday = true;

            DateTime? last = lastByEntryId.TryGetValue(entry.Id, out var l) ? l : null;

            switch (entry.Mode)
            {
                case ScheduleMode.Interval:
                    if (IsIntervalDue(entry, now, last, out bool tooSoon)) due.Add(entry.Id);
                    else if (tooSoon) anyInWindowTooSoon = true;
                    break;

                case ScheduleMode.SpecificTimes:
                    if (IsSpecificTimeDue(entry, now, last)) due.Add(entry.Id);
                    break;
            }
        }

        if (due.Count > 0) return new ScheduleEvaluation(Reason.ShouldCapture, due.ToArray());
        if (!anyActiveToday) return new ScheduleEvaluation(Reason.OutsideActiveDay, Array.Empty<string>());
        if (anyInWindowTooSoon) return new ScheduleEvaluation(Reason.TooSoon, Array.Empty<string>());
        return new ScheduleEvaluation(Reason.OutsideTimeWindows, Array.Empty<string>());
    }

    /// <summary>
    /// In-window and the interval has elapsed (or no prior capture) → due. In-window but too soon
    /// since the last capture → <paramref name="tooSoon"/> is set. Outside the window → neither.
    /// </summary>
    private static bool IsIntervalDue(ScheduleEntry entry, DateTime now, DateTime? last, out bool tooSoon)
    {
        tooSoon = false;
        if (!entry.Window.Contains(TimeOnly.FromDateTime(now))) return false;

        if (last is { } l && (now - l).TotalSeconds < entry.IntervalSeconds)
        {
            tooSoon = true;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Due if the current tick lands in <c>[P, P + tolerance)</c> for some listed point P and we
    /// have not already fired this exact occurrence. The dedup key is the fully-qualified scheduled
    /// instant (<c>today + P</c>), so yesterday's same-HH:mm capture never suppresses today's.
    /// </summary>
    private static bool IsSpecificTimeDue(ScheduleEntry entry, DateTime now, DateTime? last)
    {
        var instant = FiringInstant(entry, now);
        return instant is not null && last != instant;
    }

    /// <summary>
    /// The scheduled instant (<c>today + P</c>) of the SpecificTimes point that the current tick
    /// falls within, ignoring dedup; <c>null</c> if no point is in range. Shared by the decision
    /// (which then applies dedup) and the scheduler (which stamps this exact instant on fire),
    /// so both agree on which occurrence fired.
    /// </summary>
    internal static DateTime? FiringInstant(ScheduleEntry entry, DateTime now)
    {
        var nowTime = TimeOnly.FromDateTime(now);
        foreach (var p in entry.Times)
        {
            if (nowTime < p) continue;
            if (nowTime.ToTimeSpan() - p.ToTimeSpan() >= SpecificTimeTolerance) continue;
            return now.Date + p.ToTimeSpan();
        }
        return null;
    }
}
