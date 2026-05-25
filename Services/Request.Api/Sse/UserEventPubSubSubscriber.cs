using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ReportingPlatform.RequestApi.Sse;

/// <summary>
/// BackgroundService that subscribes to two Redis pub/sub patterns and fans out JSON
/// SSE events to active <c>GET /sse/events</c> clients via <see cref="UserSseRegistry"/>.
/// <list type="bullet">
///   <item><c>rp:sse-user-event:{userId}</c> — terminal results published by Response.Dispatcher.
///     The value is a JSON object containing <c>eventType</c> (RequestCompleted | RequestFailed | RequestCancelled)
///     plus the full result payload.</item>
///   <item><c>rp:sse-widget-event:{widgetChannel}</c> — WidgetStale notifications published by
///     Event.Processor. The value is a JSON object with <c>channel</c>, <c>reason</c>, <c>updatedAt</c>.</item>
/// </list>
/// </summary>
public sealed class UserEventPubSubSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer          _mux;
    private readonly UserSseRegistry                 _registry;
    private readonly ILogger<UserEventPubSubSubscriber> _logger;

    public UserEventPubSubSubscriber(
        IConnectionMultiplexer mux,
        UserSseRegistry registry,
        ILogger<UserEventPubSubSubscriber> logger)
    {
        _mux      = mux;
        _registry = registry;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = _mux.GetSubscriber();

        // ── Terminal request results (RequestCompleted / RequestFailed / RequestCancelled) ─
        await sub.SubscribeAsync(
            RedisChannel.Pattern("rp:sse-user-event:*"),
            (channel, value) =>
            {
                var userId = ExtractSuffix(channel, "rp:sse-user-event:");
                if (userId is null || value.IsNullOrEmpty) return;

                // Extract eventType from the published JSON so the SSE event: header is set correctly.
                var eventType = "RequestCompleted";
                try
                {
                    using var doc = JsonDocument.Parse(value.ToString());
                    eventType = doc.RootElement.GetProperty("eventType").GetString()
                             ?? "RequestCompleted";
                }
                catch
                {
                    // Malformed JSON — use default event type; client will see an unexpected shape.
                }

                _registry.FanOut(userId, new SseEvent(eventType, value!));
            });

        // ── Widget-stale notifications (targeted — requires client to subscribe widgetChannel) ──
        await sub.SubscribeAsync(
            RedisChannel.Pattern("rp:sse-widget-event:*"),
            (channel, value) =>
            {
                var widgetChannel = ExtractSuffix(channel, "rp:sse-widget-event:");
                if (widgetChannel is null || value.IsNullOrEmpty) return;

                _registry.FanOut(widgetChannel, new SseEvent("WidgetStale", value!));
            });

        // ── Global broadcast (sent to ALL connected clients on this node) ─────────────────────
        // Published to rp:sse-global-event with JSON: { eventType, ...payload }
        // Used by NotifyStale endpoint so screens in SSE mode refresh without requiring
        // the client to reconnect with a ?widgetChannel param.
        await sub.SubscribeAsync(
            RedisChannel.Literal("rp:sse-global-event"),
            (_, value) =>
            {
                if (value.IsNullOrEmpty) return;

                var eventType = "WidgetStale";
                try
                {
                    using var doc = JsonDocument.Parse(value.ToString());
                    if (doc.RootElement.TryGetProperty("eventType", out var et))
                        eventType = et.GetString() ?? eventType;
                }
                catch { /* malformed JSON — keep default */ }

                _registry.BroadcastAll(new SseEvent(eventType, value!));
            });

        _logger.LogInformation("SSE user-event pub/sub subscriber active");

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        await sub.UnsubscribeAllAsync();
    }

    private static string? ExtractSuffix(RedisChannel channel, string prefix)
    {
        var name = channel.ToString();
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : null;
    }
}
