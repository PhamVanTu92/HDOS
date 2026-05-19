namespace ReportingPlatform.Bridge.Bridge;

public sealed class HeartbeatMonitor : IAsyncDisposable
{
    private long _lastHeartbeatTicks = DateTimeOffset.UtcNow.Ticks;
    private readonly Timer  _timer;
    private readonly Func<Task> _onTimeout;

    public HeartbeatMonitor(Func<Task> onTimeout)
    {
        _onTimeout = onTimeout;
        _timer     = new Timer(Check, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void RecordHeartbeat()
    {
        Interlocked.Exchange(ref _lastHeartbeatTicks, DateTimeOffset.UtcNow.Ticks);
    }

    private void Check(object? _)
    {
        var lastTicks = Interlocked.Read(ref _lastHeartbeatTicks);
        var elapsed   = DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero);
        if (elapsed.TotalSeconds > 30)
            _ = _onTimeout();
    }

    public async ValueTask DisposeAsync()
    {
        await _timer.DisposeAsync();
    }
}
