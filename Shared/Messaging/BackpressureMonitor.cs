using Microsoft.Extensions.Logging;

namespace ReportingPlatform.Messaging;

// Tracks in-flight request count for a single consumer and signals backpressure
// when the configured threshold is reached. Thread-safe.
public sealed class BackpressureMonitor
{
    private readonly int _highWaterMark;
    private readonly ILogger<BackpressureMonitor> _logger;
    private int _inflight;

    public BackpressureMonitor(int highWaterMark, ILogger<BackpressureMonitor> logger)
    {
        _highWaterMark = highWaterMark;
        _logger        = logger;
    }

    public bool IsUnderPressure => Volatile.Read(ref _inflight) >= _highWaterMark;

    public IDisposable Track()
    {
        var current = Interlocked.Increment(ref _inflight);
        if (current == _highWaterMark)
            _logger.LogWarning("Backpressure threshold reached: {Inflight}/{HighWaterMark}", current, _highWaterMark);
        return new Tracker(this);
    }

    private void Decrement() => Interlocked.Decrement(ref _inflight);

    private sealed class Tracker(BackpressureMonitor owner) : IDisposable
    {
        public void Dispose() => owner.Decrement();
    }
}
