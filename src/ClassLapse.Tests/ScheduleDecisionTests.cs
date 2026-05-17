using ClassLapse.Core;
using ClassLapse.Models;
using Xunit;

namespace ClassLapse.Tests;

public class ScheduleDecisionTests
{
    private static ScheduleConfig WeekdaysEightToFive() => new()
    {
        ActiveDays = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday,
        },
        TimeWindows = new[]
        {
            new TimeWindow(new TimeOnly(8, 0), new TimeOnly(17, 0)),
        },
        IntervalSeconds = 30,
    };

    private static ScheduleConfig MorningAndAfternoon() => new()
    {
        ActiveDays = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday,
        },
        TimeWindows = new[]
        {
            new TimeWindow(new TimeOnly(8, 0), new TimeOnly(11, 30)),
            new TimeWindow(new TimeOnly(13, 30), new TimeOnly(17, 0)),
        },
        IntervalSeconds = 30,
    };

    // 2026-05-17 is a Sunday; 2026-05-18 is Monday.

    [Fact]
    public void Sunday_during_work_hours_is_OutsideActiveDay()
    {
        var sundayNoon = new DateTime(2026, 5, 17, 12, 0, 0);

        var result = ScheduleDecision.Evaluate(sundayNoon, null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.OutsideActiveDay, result);
    }

    [Fact]
    public void Monday_one_second_before_start_is_OutsideTimeWindows()
    {
        var mondayJustBefore = new DateTime(2026, 5, 18, 7, 59, 59);

        var result = ScheduleDecision.Evaluate(mondayJustBefore, null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, result);
    }

    [Fact]
    public void Monday_exactly_at_start_time_is_ShouldCapture()
    {
        var mondayAtStart = new DateTime(2026, 5, 18, 8, 0, 0);

        var result = ScheduleDecision.Evaluate(mondayAtStart, null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result);
    }

    [Fact]
    public void Monday_one_second_before_end_is_ShouldCapture()
    {
        var mondayJustBeforeEnd = new DateTime(2026, 5, 18, 16, 59, 59);

        var result = ScheduleDecision.Evaluate(mondayJustBeforeEnd, null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result);
    }

    [Fact]
    public void Monday_exactly_at_end_time_is_OutsideTimeWindows()
    {
        // End is exclusive: 17:00:00 means "do not capture at or after 17:00".
        var mondayAtEnd = new DateTime(2026, 5, 18, 17, 0, 0);

        var result = ScheduleDecision.Evaluate(mondayAtEnd, null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, result);
    }

    [Fact]
    public void Within_interval_since_last_capture_is_TooSoon()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 20);
        var last = new DateTime(2026, 5, 18, 10, 0, 0);

        var result = ScheduleDecision.Evaluate(now, last, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.TooSoon, result);
    }

    [Fact]
    public void Exactly_at_interval_boundary_is_ShouldCapture()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 30);
        var last = new DateTime(2026, 5, 18, 10, 0, 0);

        var result = ScheduleDecision.Evaluate(now, last, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result);
    }

    [Fact]
    public void No_previous_capture_returns_ShouldCapture()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);

        var result = ScheduleDecision.Evaluate(now, lastCaptureAt: null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result);
    }

    [Fact]
    public void Paused_in_future_returns_Paused_even_inside_window()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 18, 11, 0, 0);

        var result = ScheduleDecision.Evaluate(now, null, WeekdaysEightToFive(), pausedUntil);

        Assert.Equal(ScheduleDecision.Reason.Paused, result);
    }

    [Fact]
    public void Paused_in_past_is_ignored()
    {
        var now = new DateTime(2026, 5, 18, 10, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 18, 9, 0, 0);

        var result = ScheduleDecision.Evaluate(now, null, WeekdaysEightToFive(), pausedUntil);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result);
    }

    [Fact]
    public void Paused_takes_priority_over_OutsideActiveDay()
    {
        var sundayDuringDay = new DateTime(2026, 5, 17, 12, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 17, 13, 0, 0);

        var result = ScheduleDecision.Evaluate(sundayDuringDay, null, WeekdaysEightToFive(), pausedUntil);

        Assert.Equal(ScheduleDecision.Reason.Paused, result);
    }

    // --- multi-window cases ---

    [Fact]
    public void Noon_between_morning_and_afternoon_is_OutsideTimeWindows()
    {
        // Mon 12:00 — students at lunch, both windows closed.
        var lunch = new DateTime(2026, 5, 18, 12, 0, 0);

        var result = ScheduleDecision.Evaluate(lunch, null, MorningAndAfternoon(), null);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, result);
    }

    [Fact]
    public void End_of_morning_window_is_OutsideTimeWindows()
    {
        // 11:30 exactly — exclusive end of first window, not yet in second.
        var endOfMorning = new DateTime(2026, 5, 18, 11, 30, 0);

        var result = ScheduleDecision.Evaluate(endOfMorning, null, MorningAndAfternoon(), null);

        Assert.Equal(ScheduleDecision.Reason.OutsideTimeWindows, result);
    }

    [Fact]
    public void Start_of_afternoon_window_is_ShouldCapture()
    {
        // 13:30 exactly — inclusive start of second window.
        var startOfAfternoon = new DateTime(2026, 5, 18, 13, 30, 0);

        var result = ScheduleDecision.Evaluate(startOfAfternoon, null, MorningAndAfternoon(), null);

        Assert.Equal(ScheduleDecision.Reason.ShouldCapture, result);
    }

    [Fact]
    public void Inside_either_window_is_ShouldCapture()
    {
        var morningInside = new DateTime(2026, 5, 18, 9, 0, 0);
        var afternoonInside = new DateTime(2026, 5, 18, 15, 0, 0);

        Assert.Equal(
            ScheduleDecision.Reason.ShouldCapture,
            ScheduleDecision.Evaluate(morningInside, null, MorningAndAfternoon(), null));
        Assert.Equal(
            ScheduleDecision.Reason.ShouldCapture,
            ScheduleDecision.Evaluate(afternoonInside, null, MorningAndAfternoon(), null));
    }
}
