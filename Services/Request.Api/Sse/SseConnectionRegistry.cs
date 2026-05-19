using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ReportingPlatform.RequestApi.Sse;

/// <summary>
/// Thread-safe registry of active SSE connections per requestId.
/// Each Request.Api node maintains its own local registry — cross-node fan-out
/// is handled by Redis pub/sub (<see cref="ProgressPubSubSubscriber"/>).
/// </summary>
public sealed class SseConnectionRegistry
{
    private readonly ConcurrentDictionary<string, List<ChannelWriter<SseEvent>>> _connections = new();
    private readonly ILogger<SseConnectionRegistry> _logger;

    public SseConnectionRegistry(ILogger<SseConnectionRegistry> logger) => _logger = logger;

    public void Register(string requestId, ChannelWriter<SseEvent> writer)
    {
        _connections.AddOrUpdate(
            requestId,
            _ => [writer],
            (_, existing) => { lock (existing) { existing.Add(writer); return existing; } });

        _logger.LogDebug("SSE registered requestId={RequestId}", requestId);
    }

    public void Unregister(string requestId, ChannelWriter<SseEvent> writer)
    {
        if (!_connections.TryGetValue(requestId, out var list)) return;
        lock (list)
        {
            list.Remove(writer);
            if (list.Count == 0)
                _connections.TryRemove(requestId, out _);
        }
        _logger.LogDebug("SSE unregistered requestId={RequestId}", requestId);
    }

    /// <summary>
    /// Fan out an event to all local SSE connections for this requestId.
    /// Called by <see cref="ProgressPubSubSubscriber"/> from the Redis pub/sub callback.
    /// Non-blocking: drops oldest if the channel is full.
    /// </summary>
    public void FanOut(string requestId, SseEvent evt)
    {
        if (!_connections.TryGetValue(requestId, out var list)) return;
        List<ChannelWriter<SseEvent>> snapshot;
        lock (list) { snapshot = [..list]; }

        foreach (var w in snapshot)
            w.TryWrite(evt); // non-blocking; BoundedChannel drops oldest if full
    }

    /// <summary>Returns the count of active SSE connections across all requestIds.</summary>
    public int TotalConnections =>
        _connections.Values.Sum(list => { lock (list) { return list.Count; } });
}
