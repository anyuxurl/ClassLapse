using ClassLapse.Models;

namespace ClassLapse.Core;

/// <summary>
/// Drives the per-second tick loop. Holds no decision logic itself —
/// it asks <see cref="ScheduleDecision.Evaluate"/> and dispatches to <see cref="OnCaptureRequested"/>
/// when the answer is <see cref="ScheduleDecision.Reason.ShouldCapture"/>.
/// </summary>
public sealed class CaptureScheduler : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly Func<DateTime> _clock;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _tickGate = new(initialCount: 1, maxCount: 1);
    private readonly Dictionary<string, DateTime> _lastByEntryId = new();
    private volatile bool _disposed;

    public event Func<AppConfig, Task>? OnCaptureRequested;
    public event Action<ScheduleDecision.Reason, AppConfig>? OnTickEvaluated;

    public CaptureScheduler(ConfigStore configStore, Func<DateTime>? clock = null)
    {
        _configStore = configStore;
        _clock = clock ?? (() => DateTime.Now);
        _timer = new Timer(TickCallback, state: null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(TimeSpan? tickPeriod = null)
    {
        var period = tickPeriod ?? TimeSpan.FromSeconds(1);
        _timer.Change(TimeSpan.Zero, period);
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        _tickGate.Dispose();
    }

    private async void TickCallback(object? state)
    {
        // Re-entrancy guard: a capture that takes >1s must not stack ticks.
        if (!await _tickGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (_disposed) return;
            await EvaluateAndDispatchAsync().ConfigureAwait(false);
        }
        catch
        {
            // Swallow; failures are reported via the capture handler's own logging.
        }
        finally
        {
            if (!_disposed) _tickGate.Release();
        }
    }

    private async Task EvaluateAndDispatchAsync()
    {
        var config = _configStore.Load();
        var now = _clock();

        PruneStaleEntries(config.Schedule);
        var eval = ScheduleDecision.Evaluate(now, config.Schedule, config.PausedUntil, _lastByEntryId);

        OnTickEvaluated?.Invoke(eval.Reason, config);

        if (eval.DueEntryIds.Length == 0) return;

        // Stamp before the (slow) capture so a capture that overruns the next tick is not re-fired.
        // Matches the old single-clock behaviour: a failed capture still consumes the interval slot.
        StampDueEntries(config.Schedule, eval.DueEntryIds, now);

        var handler = OnCaptureRequested;
        if (handler == null) return;

        await handler.Invoke(config).ConfigureAwait(false);
    }

    /// <summary>Drop per-entry timing for entries that no longer exist (deleted/renamed via settings).</summary>
    private void PruneStaleEntries(ScheduleConfig schedule)
    {
        if (_lastByEntryId.Count == 0) return;

        var live = new HashSet<string>(schedule.Entries.Length);
        foreach (var e in schedule.Entries) live.Add(e.Id);

        List<string>? stale = null;
        foreach (var key in _lastByEntryId.Keys)
        {
            if (!live.Contains(key)) (stale ??= new List<string>()).Add(key);
        }
        if (stale != null)
        {
            foreach (var k in stale) _lastByEntryId.Remove(k);
        }
    }

    /// <summary>
    /// Record each due entry's capture time. Interval entries store the wall clock; SpecificTimes
    /// entries store the scheduled instant so the decision's dedup key matches on the next tick.
    /// Non-due entries are deliberately left untouched — each entry's cadence is independent.
    /// </summary>
    private void StampDueEntries(ScheduleConfig schedule, string[] dueIds, DateTime now)
    {
        foreach (var id in dueIds)
        {
            var entry = Array.Find(schedule.Entries, e => e.Id == id);
            if (entry is null) continue;
            _lastByEntryId[id] = entry.Mode == ScheduleMode.SpecificTimes
                ? (ScheduleDecision.FiringInstant(entry, now) ?? now)
                : now;
        }
    }
}
