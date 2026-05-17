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
    private DateTime? _lastCaptureAt;
    private volatile bool _disposed;

    public event Func<AppConfig, Task>? OnCaptureRequested;
    public event Action<ScheduleDecision.Reason, AppConfig>? OnTickEvaluated;

    public CaptureScheduler(ConfigStore configStore, Func<DateTime>? clock = null)
    {
        _configStore = configStore;
        _clock = clock ?? (() => DateTime.Now);
        _timer = new Timer(TickCallback, state: null, Timeout.Infinite, Timeout.Infinite);
    }

    public DateTime? LastCaptureAt => _lastCaptureAt;

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
        var reason = ScheduleDecision.Evaluate(now, _lastCaptureAt, config.Schedule, config.PausedUntil);

        OnTickEvaluated?.Invoke(reason, config);

        if (reason != ScheduleDecision.Reason.ShouldCapture) return;

        _lastCaptureAt = now;
        var handler = OnCaptureRequested;
        if (handler == null) return;

        await handler.Invoke(config).ConfigureAwait(false);
    }
}
