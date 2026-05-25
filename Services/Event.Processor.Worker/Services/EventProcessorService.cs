using System.Text.Json;

namespace ReportingPlatform.EventProcessor.Services;

/// <summary>
/// Core domain logic for processing ingested events:
/// 1. Lookup matching widgets from <c>event_subscriptions</c> (direct indexed query).
/// 2. Dispatch <c>WidgetStale</c> via SignalR backplane.
/// 3. Publish L1 cache invalidation message to Redis (Option A — Patch 2).
/// </summary>
internal sealed class EventProcessorService
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IEventSubscriptionRepository _subscriptions;
    private readonly IHubContext<MainHub, IMainHubClient> _hub;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<EventProcessorService> _logger;

    public EventProcessorService(
        IEventSubscriptionRepository subscriptions,
        IHubContext<MainHub, IMainHubClient> hub,
        IConnectionMultiplexer redis,
        ILogger<EventProcessorService> logger)
    {
        _subscriptions = subscriptions;
        _hub           = hub;
        _redis         = redis;
        _logger        = logger;
    }

    public async Task ProcessAsync(IngestEventEnvelope evt, CancellationToken ct = default)
    {
        var subscribers = await _subscriptions.GetSubscribersAsync(evt.TenantId, evt.EventType, ct);

        if (subscribers.Count == 0)
        {
            _logger.LogDebug(
                "No widget subscriptions for event {EventType} in tenant {TenantId}",
                evt.EventType, evt.TenantId);
            return;
        }

        _logger.LogInformation(
            "Processing event {EventType} for tenant {TenantId}: {Count} widget(s) stale",
            evt.EventType, evt.TenantId, subscribers.Count);

        var hint = new WidgetStaleHint
        {
            Reason    = WidgetStaleReasons.DataUpdated,
            UpdatedAt = evt.OccurredAt,
        };

        foreach (var sub in subscribers)
        {
            var group = WidgetGroup(sub.DashboardCode, sub.WidgetId);

            try
            {
                await _hub.Clients.Group(group).WidgetStale(group, hint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to dispatch WidgetStale for group {Group}", group);
            }

            // Option A (Patch 2): publish L1 cache invalidation.
            try
            {
                var channel = RedisChannel.Literal(
                    $"rp:cache-invalidate:widget:{evt.TenantId}:{sub.DashboardCode}:{sub.WidgetId}");
                await _redis.GetSubscriber().PublishAsync(channel, RedisValue.EmptyString);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to publish cache invalidation for {TenantId}/{DashCode}/{WidgetId}",
                    evt.TenantId, sub.DashboardCode, sub.WidgetId);
            }

            // SSE widget-event: publish to rp:sse-widget-event:{group} so browsers
            // connected to GET /sse/events with ?widgetChannel={group} receive WidgetStale.
            try
            {
                var ssePayload = JsonSerializer.Serialize(new
                {
                    channel   = group,
                    reason    = hint.Reason,
                    updatedAt = hint.UpdatedAt,
                }, _jsonOpts);

                await _redis.GetSubscriber().PublishAsync(
                    RedisChannel.Literal($"rp:sse-widget-event:{group}"),
                    ssePayload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to publish SSE widget event for group {Group}", group);
            }
        }

        // Global SSE broadcast: notify all SSE-mode report screens that underlying data
        // has changed. Report screens listen for channel="screen:all" and refresh their
        // widgets immediately — the same real-time push that Dashboard widgets receive.
        // This fires once per event (not once per subscriber) to avoid fan-out amplification.
        try
        {
            var globalPayload = JsonSerializer.Serialize(new
            {
                eventType = "WidgetStale",
                channel   = "screen:all",
                reason    = "data.updated",
                updatedAt = hint.UpdatedAt,
            }, _jsonOpts);

            await _redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal("rp:sse-global-event"),
                globalPayload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish global SSE screen-refresh event");
        }
    }

    private static string WidgetGroup(string dashboardCode, string widgetId)
        => $"widget:{dashboardCode}:{widgetId}";
}
