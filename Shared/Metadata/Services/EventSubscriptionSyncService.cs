using ReportingPlatform.Contracts.Definitions;
using ReportingPlatform.Metadata.Abstractions;

namespace ReportingPlatform.Metadata.Services;

/// <summary>
/// Keeps <c>event_subscriptions</c> in sync with <c>WidgetDefinition.SubscribesTo</c>
/// whenever a dashboard definition is upserted.
///
/// Called by <c>MetadataDashboardUpsertHandler</c> after a successful upsert.
/// Performs: DELETE existing subscriptions for the dashboard + INSERT new ones —
/// all in a single transaction (delegated to <see cref="IEventSubscriptionRepository"/>).
/// </summary>
public sealed class EventSubscriptionSyncService
{
    private readonly IEventSubscriptionRepository _repo;
    private readonly ILogger<EventSubscriptionSyncService> _logger;

    public EventSubscriptionSyncService(
        IEventSubscriptionRepository repo,
        ILogger<EventSubscriptionSyncService> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public async Task SyncAsync(
        string tenantId,
        DashboardDefinition definition,
        CancellationToken ct = default)
    {
        var subscriptions = BuildSubscriptions(definition);

        _logger.LogDebug(
            "Syncing event subscriptions: tenant={TenantId} dashboard={DashboardCode} count={Count}",
            tenantId, definition.DashboardCode, subscriptions.Count);

        await _repo.SyncAsync(tenantId, definition.DashboardCode, subscriptions, ct);
    }

    private static List<(string WidgetId, string EventType)> BuildSubscriptions(DashboardDefinition def)
    {
        var result = new List<(string, string)>();

        if (def.Widgets is null)
            return result;

        foreach (var widget in def.Widgets)
        {
            if (widget.SubscribesTo is null or { Count: 0 })
                continue;

            foreach (var eventType in widget.SubscribesTo)
            {
                if (!string.IsNullOrWhiteSpace(eventType))
                    result.Add((widget.WidgetId, eventType));
            }
        }

        return result;
    }
}
