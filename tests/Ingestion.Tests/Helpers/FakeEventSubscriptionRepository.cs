using ReportingPlatform.Metadata.Abstractions;

namespace ReportingPlatform.Ingestion.Tests.Helpers;

internal sealed class FakeEventSubscriptionRepository : IEventSubscriptionRepository
{
    private readonly Dictionary<(string TenantId, string EventType), List<EventSubscriptionRow>> _store = new();

    public void Seed(string tenantId, string eventType, params EventSubscriptionRow[] rows)
    {
        var key = (tenantId, eventType);
        if (!_store.ContainsKey(key))
            _store[key] = [];
        _store[key].AddRange(rows);
    }

    public Task<IReadOnlyList<EventSubscriptionRow>> GetSubscribersAsync(
        string tenantId, string eventType, CancellationToken ct = default)
    {
        var key = (tenantId, eventType);
        IReadOnlyList<EventSubscriptionRow> result = _store.TryGetValue(key, out var rows)
            ? rows
            : [];
        return Task.FromResult(result);
    }

    public Task SyncAsync(
        string tenantId,
        string dashboardCode,
        IReadOnlyList<(string WidgetId, string EventType)> subscriptions,
        CancellationToken ct = default)
    {
        // Remove existing for this dashboard.
        var toRemove = _store.Keys
            .Where(k => k.TenantId == tenantId)
            .ToList();

        foreach (var k in toRemove)
            _store[k].RemoveAll(r => r.DashboardCode == dashboardCode);

        // Insert new.
        foreach (var (widgetId, eventType) in subscriptions)
        {
            var key = (tenantId, eventType);
            if (!_store.ContainsKey(key))
                _store[key] = [];
            _store[key].Add(new EventSubscriptionRow
            {
                DashboardCode = dashboardCode,
                WidgetId      = widgetId,
            });
        }

        return Task.CompletedTask;
    }
}
