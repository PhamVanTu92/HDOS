using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ReportingPlatform.RequestApi.Sse;

/// <summary>
/// Thread-safe registry of active <c>GET /sse/events</c> connections.
/// Keyed by any subscription key: userId (for terminal request-result events) or
/// widgetChannel string (for WidgetStale notifications).
/// Each Request.Api node holds only its own local connections; cross-node fan-out
/// is handled by Redis pub/sub in <see cref="UserEventPubSubSubscriber"/>.
/// </summary>
public sealed class UserSseRegistry
{
    private readonly ConcurrentDictionary<string, List<ChannelWriter<SseEvent>>> _connections = new();
    private readonly ILogger<UserSseRegistry> _logger;

    public UserSseRegistry(ILogger<UserSseRegistry> logger) => _logger = logger;

    public void Register(string key, ChannelWriter<SseEvent> writer)
    {
        _connections.AddOrUpdate(
            key,
            _ => [writer],
            (_, existing) => { lock (existing) { existing.Add(writer); return existing; } });

        _logger.LogDebug("SSE events registered key={Key}", key);
    }

    public void Unregister(string key, ChannelWriter<SseEvent> writer)
    {
        if (!_connections.TryGetValue(key, out var list)) return;
        lock (list)
        {
            list.Remove(writer);
            if (list.Count == 0)
                _connections.TryRemove(key, out _);
        }
        _logger.LogDebug("SSE events unregistered key={Key}", key);
    }

    /// <summary>
    /// Fan out an event to all local SSE connections registered under <paramref name="key"/>.
    /// Non-blocking: the bounded channel drops oldest entries if full.
    /// </summary>
    public void FanOut(string key, SseEvent evt)
    {
        if (!_connections.TryGetValue(key, out var list)) return;
        List<ChannelWriter<SseEvent>> snapshot;
        lock (list) { snapshot = [..list]; }

        foreach (var w in snapshot)
            w.TryWrite(evt);
    }

    /// <summary>Total active SSE /events connections across all keys on this node.</summary>
    public int TotalConnections =>
        _connections.Values.Sum(list => { lock (list) { return list.Count; } });
}
