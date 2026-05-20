namespace ReportingPlatform.Metadata.Abstractions;

/// <summary>Widget event subscription record returned from the lookup table.</summary>
public sealed record EventSubscriptionRow
{
    public required string DashboardCode { get; init; }
    public required string WidgetId { get; init; }
}

public interface IEventSubscriptionRepository
{
    /// <summary>
    /// Returns all widgets subscribed to <paramref name="eventType"/> for the given tenant.
    /// Direct indexed lookup — O(1) table access, not a full scan.
    /// </summary>
    Task<IReadOnlyList<EventSubscriptionRow>> GetSubscribersAsync(
        string tenantId, string eventType, CancellationToken ct = default);

    /// <summary>
    /// Replaces all subscriptions for the given dashboard with the provided set.
    /// Called by EventSubscriptionSyncService on dashboard upsert.
    /// Performs: DELETE WHERE (tenant_id, dashboard_code) + batch INSERT.
    /// </summary>
    Task SyncAsync(
        string tenantId,
        string dashboardCode,
        IReadOnlyList<(string WidgetId, string EventType)> subscriptions,
        CancellationToken ct = default);
}
