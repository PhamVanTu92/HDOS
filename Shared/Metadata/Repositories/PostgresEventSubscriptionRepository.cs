using Dapper;
using Npgsql;
using ReportingPlatform.Metadata.Abstractions;

namespace ReportingPlatform.Metadata.Repositories;

public sealed class PostgresEventSubscriptionRepository : IEventSubscriptionRepository
{
    private readonly NpgsqlDataSource _db;

    public PostgresEventSubscriptionRepository(NpgsqlDataSource db)
        => _db = db;

    public async Task<IReadOnlyList<EventSubscriptionRow>> GetSubscribersAsync(
        string tenantId, string eventType, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<EventSubscriptionRow>(
            """
            SELECT dashboard_code AS DashboardCode, widget_id AS WidgetId
            FROM   event_subscriptions
            WHERE  tenant_id  = @tenantId
              AND  event_type = @eventType
            """,
            new { tenantId, eventType });
        return rows.AsList();
    }

    public async Task SyncAsync(
        string tenantId,
        string dashboardCode,
        IReadOnlyList<(string WidgetId, string EventType)> subscriptions,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Remove existing subscriptions for this dashboard.
        await conn.ExecuteAsync(
            """
            DELETE FROM event_subscriptions
            WHERE  tenant_id      = @tenantId
              AND  dashboard_code = @dashboardCode
            """,
            new { tenantId, dashboardCode },
            transaction: tx);

        // Insert new subscriptions (if any).
        if (subscriptions.Count > 0)
        {
            foreach (var (widgetId, eventType) in subscriptions)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO event_subscriptions (tenant_id, event_type, dashboard_code, widget_id)
                    VALUES (@tenantId, @eventType, @dashboardCode, @widgetId)
                    ON CONFLICT DO NOTHING
                    """,
                    new { tenantId, eventType, dashboardCode, widgetId },
                    transaction: tx);
            }
        }

        await tx.CommitAsync(ct);
    }
}
