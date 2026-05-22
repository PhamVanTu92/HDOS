namespace ReportingPlatform.Bridge.Bridge;

public sealed class HeartbeatMonitor : IAsyncDisposable
{
    private long _lastHeartbeatTicks = DateTimeOffset.UtcNow.Ticks;
    private readonly Timer  _timer;
    private readonly Func<Task> _onTimeout;
    private readonly double _timeoutSeconds;

    // Default: tolerate ~3 missed heartbeats (provider sends its first beat one full
    // interval after Welcome, so the threshold MUST exceed the interval — otherwise
    // every session is killed at exactly the interval mark before the first beat lands).
    public HeartbeatMonitor(Func<Task> onTimeout, double timeoutSeconds = 90)
    {
        _onTimeout      = onTimeout;
        _timeoutSeconds = timeoutSeconds;
        _timer          = new Timer(Check, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void RecordHeartbeat()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, DateTimeOffset.UtcNow.Ticks);
    }

    private void Check(object? _)
    {
        var lastTicks = Interlocked.Read(ref _lastHeartbeatTicks);
        var elapsed   = DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero);
        if (elapsed.TotalSeconds > _timeoutSeconds)
            _ = _onTimeout();
    }

    public async ValueTask DisposeAsync()
    {
        await _timer.DisposeAsync();
    }
}
