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
        StartTime = new TimeOnly(8, 0),
        EndTime = new TimeOnly(17, 0),
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
    public void Monday_one_second_before_start_is_BeforeWindow()
    {
        var mondayJustBefore = new DateTime(2026, 5, 18, 7, 59, 59);

        var result = ScheduleDecision.Evaluate(mondayJustBefore, null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.BeforeWindow, result);
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
    public void Monday_exactly_at_end_time_is_AfterWindow()
    {
        // End is exclusive: 17:00:00 means "do not capture at or after 17:00".
        var mondayAtEnd = new DateTime(2026, 5, 18, 17, 0, 0);

        var result = ScheduleDecision.Evaluate(mondayAtEnd, null, WeekdaysEightToFive(), null);

        Assert.Equal(ScheduleDecision.Reason.AfterWindow, result);
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
        // Sunday + paused-in-future: Paused should win since pause is the more recent user action.
        var sundayDuringDay = new DateTime(2026, 5, 17, 12, 0, 0);
        var pausedUntil = new DateTime(2026, 5, 17, 13, 0, 0);

        var result = ScheduleDecision.Evaluate(sundayDuringDay, null, WeekdaysEightToFive(), pausedUntil);

        Assert.Equal(ScheduleDecision.Reason.Paused, result);
    }
}
