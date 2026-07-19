using System.Collections.Generic;
using ClassLapse.Core;
using ClassLapse.Models;
using Xunit;

namespace ClassLapse.Tests;

public class ScheduleDecisionTests
{
    // 2026-05-17 is a Sunday; 2026-05-18 is a Monday.

    private static readonly DayOfWeek[] Weekdays =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
    };

    private static readonly DayOfWeek[] AllDays =
    {
        DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    };

    private static readonly IReadOnlyDictionary<string, DateTime> NoCaptures =
        new Dictionary<string, DateTime>();

    // ----- builders -----

    private static ScheduleEntry Interval(string id, TimeOnly start, TimeOnly end, int seconds, DayOfWeek[]? days = null)
        => new()
        {
            Id = id,
            Mode = ScheduleMode.Interval,
            ActiveDays = days ?? Weekdays,
            Window = new TimeWindow(start, end),
            IntervalSeconds = seconds,
        };

    private static ScheduleEntry Specific(string id, DayOfWeek[]? days, params TimeOnly[] times)
        => new()
        {
            Id = id,
            Mode = ScheduleMode.SpecificTimes,
            ActiveDays = days ?? AllDays,
            Times = times,
        };

    private static ScheduleConfig Schedule(params ScheduleEntry[] entries) => new() { Entries = entries };

    private static Dictionary<string, DateTime> Last(string id, DateTime when) => new() { [id] = when };

    private static ScheduleEvaluation Eval(
        DateTime now, ScheduleConfig schedule,
        DateTime? paused = null, bool pausedIndefinitely = false,
        IReadOnlyDictionary<string, DateTime>? last = null)
        => ScheduleDecision.Evaluate(now, schedule, paused, pausedIndefinitely, last ?? NoCaptures);

    private static ScheduleConfig WeekdaysEightToFive() =>
        Schedule(Interval("w", new TimeOnly(8, 0), new TimeOnly(17, 0), 30));

    private static ScheduleConfig MorningAndAfternoon() =>
        Schedule(
            Interval("am", new TimeOnly(8, 0), new TimeOnly(11, 30), 30),
            Interval("pm", new TimeOnly(13, 30), new TimeOnly(17, 0), 30));

    // ========================= interval mode =========================

    [Fact]
    public void Sunday_during_work_hours_is_OutsideActiveDay()
    {
        var sundayNoon = new DateTime(2026, 5, 17, 12, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.OutsideActiveDay, Eval(sundayNoon, WeekdaysEightToFive()).Reason);
    }

    [Fact]
    public void Monday_one_second_before_start_is_OutsideTimeWindows()
    {
        var mondayJustBefore = new DateTime(2026, 5, 18, 7, 59, 59);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, Eval(mondayJustBefore, WeekdaysEightToFive()).Reason);
    }

    [Fact]
    public void Monday_exactly_at_start_time_is_ShouldCapture()
    {
        var mondayAtStart = new DateTime(2026, 5, 18, 8, 0, 0);

        var result = Eval(mondayAtStart, WeekdaysEightToFive());

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result.Reason);
        Assert.Equal(new[] { "w" }, result.DueEntryIds);
    }

    [Fact]
    public void Monday_one_second_before_end_is_ShouldCapture()
    {
        var mondayJustBeforeEnd = new DateTime(2026, 5, 18, 16, 59, 59);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(mondayJustBeforeEnd, WeekdaysEightToFive()).Reason);
    }

    [Fact]
    public void Monday_exactly_at_end_time_is_OutsideTimeWindows()
    {
        // End is exclusive: 17:00:00 means "do not capture at or after 17:00".
        var mondayAtEnd = new DateTime(2026, 5, 18, 17, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, Eval(mondayAtEnd, WeekdaysEightToFive()).Reason);
    }

    [Fact]
    public void Within_interval_since_last_capture_is_TooSoon()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 20);
        var last = Last("w", new DateTime(2026, 5, 18, 10, 0, 0));

        Assert.Equal(ScheduleDecision.Reason.TooSoon, Eval(now, WeekdaysEightToFive(), last: last).Reason);
    }

    [Fact]
    public void Exactly_at_interval_boundary_is_ShouldCapture()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 30);
        var last = Last("w", new DateTime(2026, 5, 18, 10, 0, 0));

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(now, WeekdaysEightToFive(), last: last).Reason);
    }

    [Fact]
    public void No_previous_capture_returns_ShouldCapture()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(now, WeekdaysEightToFive()).Reason);
    }

    // ----- pause -----

    [Fact]
    public void Paused_in_future_returns_Paused_even_inside_window()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 18, 11, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.Paused, Eval(now, WeekdaysEightToFive(), paused: pausedUntil).Reason);
    }

    [Fact]
    public void Paused_in_past_is_ignored()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 18, 9, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(now, WeekdaysEightToFive(), paused: pausedUntil).Reason);
    }

    [Fact]
    public void Paused_takes_priority_over_OutsideActiveDay()
    {
        var sundayDuringDay = new DateTime(2026, 5, 17, 12, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 17, 13, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.Paused, Eval(sundayDuringDay, WeekdaysEightToFive(), paused: pausedUntil).Reason);
    }

    [Fact]
    public void Paused_indefinitely_returns_Paused_even_inside_window()
    {
        // Open-ended "vacation" pause: no PausedUntil, yet capture is suppressed inside the window.
        var now = new DateTime(2026, 5, 18, 10, 0, 0); // Monday, inside 08:00–17:00

        Assert.Equal(ScheduleDecision.Reason.Paused,
            Eval(now, WeekdaysEightToFive(), pausedIndefinitely: true).Reason);
    }

    [Fact]
    public void Paused_indefinitely_holds_even_after_pausedUntil_elapsed()
    {
        // The open-ended flag wins over an already-elapsed timed pause — it never auto-resumes.
        var now = new DateTime(2026, 5, 18, 10, 0, 0);
        var elapsed = new DateTime(2026, 5, 18, 9, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.Paused,
            Eval(now, WeekdaysEightToFive(), paused: elapsed, pausedIndefinitely: true).Reason);
    }

    // ----- multi-window (now multi-entry) -----

    [Fact]
    public void Noon_between_morning_and_afternoon_is_OutsideTimeWindows()
    {
        var lunch = new DateTime(2026, 5, 18, 12, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, Eval(lunch, MorningAndAfternoon()).Reason);
    }

    [Fact]
    public void End_of_morning_window_is_OutsideTimeWindows()
    {
        var endOfMorning = new DateTime(2026, 5, 18, 11, 30, 0);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, Eval(endOfMorning, MorningAndAfternoon()).Reason);
    }

    [Fact]
    public void Start_of_afternoon_window_is_ShouldCapture()
    {
        var startOfAfternoon = new DateTime(2026, 5, 18, 13, 30, 0);

        var result = Eval(startOfAfternoon, MorningAndAfternoon());

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result.Reason);
        Assert.Equal(new[] { "pm" }, result.DueEntryIds);
    }

    [Fact]
    public void Inside_either_window_is_ShouldCapture()
    {
        var morningInside = new DateTime(2026, 5, 18, 9, 0, 0);
        var afternoonInside = new DateTime(2026, 5, 18, 15, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(morningInside, MorningAndAfternoon()).Reason);
        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(afternoonInside, MorningAndAfternoon()).Reason);
    }

    [Fact]
    public void Disabled_entry_is_ignored()
    {
        var entry = Interval("w", new TimeOnly(8, 0), new TimeOnly(17, 0), 30);
        entry.Enabled = false;
        var now = new DateTime(2026, 5, 18, 10, 0, 0); // Monday, inside the (disabled) window

        // The only entry is disabled, so no entry runs today at all.
        Assert.Equal(ScheduleDecision.Reason.OutsideActiveDay, Eval(now, Schedule(entry)).Reason);
    }

    [Fact]
    public void Empty_schedule_is_OutsideActiveDay()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.OutsideActiveDay, Eval(now, Schedule()).Reason);
    }

    // ========================= aggregation priority =========================
    // Most-active state wins: an actively-capturing/in-window entry must dominate a between-windows one.

    [Fact]
    public void In_window_too_soon_beats_another_entry_between_windows()
    {
        var schedule = Schedule(
            Interval("A", new TimeOnly(8, 0), new TimeOnly(17, 0), 30),
            Interval("B", new TimeOnly(18, 0), new TimeOnly(19, 0), 30));
        var now = new DateTime(2026, 5, 18, 10, 0, 10);
        var last = Last("A", new DateTime(2026, 5, 18, 10, 0, 0)); // A captured 10s ago → too soon

        var result = Eval(now, schedule, last: last);

        Assert.Equal(ScheduleDecision.Reason.TooSoon, result.Reason); // not OutsideTimeWindows
        Assert.Empty(result.DueEntryIds);
    }

    [Fact]
    public void Any_due_entry_dominates_between_windows_entry()
    {
        var schedule = Schedule(
            Interval("A", new TimeOnly(8, 0), new TimeOnly(17, 0), 30),
            Interval("B", new TimeOnly(18, 0), new TimeOnly(19, 0), 30));
        var now = new DateTime(2026, 5, 18, 10, 0, 40);
        var last = Last("A", new DateTime(2026, 5, 18, 10, 0, 0)); // 40s ≥ 30s → due

        var result = Eval(now, schedule, last: last);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result.Reason);
        Assert.Equal(new[] { "A" }, result.DueEntryIds);
    }

    [Fact]
    public void Overlapping_entries_both_due_returns_all_due_ids()
    {
        var schedule = Schedule(
            Interval("A", new TimeOnly(8, 0), new TimeOnly(17, 0), 30),
            Interval("B", new TimeOnly(9, 0), new TimeOnly(10, 0), 10));
        var now = new DateTime(2026, 5, 18, 9, 30, 0); // inside both windows, no prior captures

        var result = Eval(now, schedule);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result.Reason);
        Assert.Equal(new[] { "A", "B" }, result.DueEntryIds);
    }

    // ========================= specific-times mode =========================

    [Fact]
    public void SpecificTime_at_exact_point_is_ShouldCapture()
    {
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 8, 0, 0);

        var result = Eval(now, schedule);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result.Reason);
        Assert.Equal(new[] { "s" }, result.DueEntryIds);
    }

    [Fact]
    public void SpecificTime_within_tolerance_is_ShouldCapture()
    {
        // Cold start 59s into the scheduled minute still fires.
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 8, 0, 59);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(now, schedule).Reason);
    }

    [Fact]
    public void SpecificTime_at_tolerance_boundary_is_not_due()
    {
        // 60s past the point → missed; [P, P+60s) is half-open.
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 8, 1, 0);

        var result = Eval(now, schedule);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, result.Reason);
        Assert.Empty(result.DueEntryIds);
    }

    [Fact]
    public void SpecificTime_before_point_is_not_due()
    {
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 7, 59, 59);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, Eval(now, schedule).Reason);
    }

    [Fact]
    public void SpecificTime_already_fired_this_minute_is_deduped()
    {
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 8, 0, 30);
        var last = Last("s", new DateTime(2026, 5, 18, 8, 0, 0)); // scheduled instant already stamped

        var result = Eval(now, schedule, last: last);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, result.Reason);
        Assert.Empty(result.DueEntryIds);
    }

    [Fact]
    public void SpecificTime_is_not_suppressed_by_yesterdays_same_time()
    {
        // The dedup key is the full instant, so 2026-05-17 08:00 must not block 2026-05-18 08:00.
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 8, 0, 10);
        var last = Last("s", new DateTime(2026, 5, 17, 8, 0, 0)); // fired yesterday

        var result = Eval(now, schedule, last: last);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result.Reason);
        Assert.Equal(new[] { "s" }, result.DueEntryIds);
    }

    [Fact]
    public void SpecificTime_second_point_fires_after_first_was_stamped()
    {
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0), new TimeOnly(8, 1)));
        var afterFirst = Last("s", new DateTime(2026, 5, 18, 8, 0, 0));

        // Still in the 08:00 minute → deduped.
        Assert.Equal(
            ScheduleDecision.Reason.OutsideTimeWindows,
            Eval(new DateTime(2026, 5, 18, 8, 0, 50), schedule, last: afterFirst).Reason);

        // Next point one minute later → fires.
        Assert.Equal(
            ScheduleDecision.Reason.ShouldCapture,
            Eval(new DateTime(2026, 5, 18, 8, 1, 10), schedule, last: afterFirst).Reason);
    }

    [Fact]
    public void SpecificTime_with_unaligned_last_still_fires_at_point()
    {
        // Robustness against a stale wall-clock last (e.g. left over from a mode flip): only an exact
        // match on the scheduled instant dedups, so an unaligned value does not wrongly suppress.
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 8, 0, 5);
        var last = Last("s", new DateTime(2026, 5, 18, 7, 30, 13)); // not the 08:00 instant

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, Eval(now, schedule, last: last).Reason);
    }

    [Fact]
    public void SpecificTime_outside_active_day_is_OutsideActiveDay()
    {
        var schedule = Schedule(Specific("s", Weekdays, new TimeOnly(8, 0)));
        var sundayAtPoint = new DateTime(2026, 5, 17, 8, 0, 0); // Sunday, not a weekday

        Assert.Equal(ScheduleDecision.Reason.OutsideActiveDay, Eval(sundayAtPoint, schedule).Reason);
    }

    [Fact]
    public void Paused_takes_priority_over_specific_time_due()
    {
        var schedule = Schedule(Specific("s", AllDays, new TimeOnly(8, 0)));
        var now = new DateTime(2026, 5, 18, 8, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 18, 9, 0, 0);

        Assert.Equal(ScheduleDecision.Reason.Paused, Eval(now, schedule, paused: pausedUntil).Reason);
    }
}
