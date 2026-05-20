using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using ReportingPlatform.HubContracts;
using ReportingPlatform.Ingestion.Tests.Helpers;
using StackExchange.Redis;

namespace ReportingPlatform.Ingestion.Tests.Workers;

/// <summary>IN10, IN11, IN12 — EventProcessorService unit tests.</summary>
public sealed class EventProcessorServiceTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (EventProcessorService Service,
        FakeEventSubscriptionRepository Repo,
        IHubContext<MainHub, IMainHubClient> Hub,
        IConnectionMultiplexer Redis,
        IMainHubClient HubClient) Build()
    {
        var repo      = new FakeEventSubscriptionRepository();
        var hubClient = Substitute.For<IMainHubClient>();
        var clients   = Substitute.For<IHubClients<IMainHubClient>>();
        clients.Group(Arg.Any<string>()).Returns(hubClient);

        var hub = Substitute.For<IHubContext<MainHub, IMainHubClient>>();
        hub.Clients.Returns(clients);

        var subscriber = Substitute.For<ISubscriber>();
        var redis      = Substitute.For<IConnectionMultiplexer>();
        redis.GetSubscriber().Returns(subscriber);

        var svc = new EventProcessorService(
            repo,
            hub,
            redis,
            NullLogger<EventProcessorService>.Instance);

        return (svc, repo, hub, redis, hubClient);
    }

    private static IngestEventEnvelope MakeEvent(
        string tenantId   = "t1",
        string eventType  = "order.shipped",
        string occurredAt = "2026-05-20T10:00:00Z")
        => new()
        {
            TenantId   = tenantId,
            EventType  = eventType,
            OccurredAt = occurredAt,
            Payload    = JsonDocument.Parse("{}").RootElement,
        };

    // ─── IN10: Matching widget → WidgetStale dispatched ────────────────────

    [Fact]
    public async Task IN10_EventProcessed_MatchingWidget_WidgetStalePublished()
    {
        var (svc, repo, _, redis, hubClient) = Build();

        repo.Seed("t1", "order.shipped",
            new EventSubscriptionRow { DashboardCode = "sales", WidgetId = "orders-chart" });

        await svc.ProcessAsync(MakeEvent());

        // WidgetStale was dispatched with the correct channel and hint.
        // hubClient is the shared mock returned by clients.Group() for all groups.
        await hubClient.Received(1).WidgetStale(
            Arg.Is<string>(c => c == "widget:sales:orders-chart"),
            Arg.Is<WidgetStaleHint>(h => h.Reason == WidgetStaleReasons.DataUpdated));

        // L1 cache invalidation published (Option A — Patch 2).
        var subscriber = redis.GetSubscriber();
        await subscriber.Received(1).PublishAsync(
            Arg.Is<RedisChannel>(c => ((string)c!).Contains("rp:cache-invalidate:widget:t1:sales:orders-chart")),
            Arg.Any<RedisValue>());
    }

    // ─── IN11: No subscription → silent discard ─────────────────────────────

    [Fact]
    public async Task IN11_EventProcessed_NoMatchingWidget_SilentlyDiscarded()
    {
        var (svc, _, hub, redis, _) = Build();
        // Repo is empty — no subscriptions for this (tenantId, eventType).

        await svc.ProcessAsync(MakeEvent(eventType: "unknown.event"));

        // No WidgetStale dispatched — Group() should never have been called.
        var hubClients = hub.Clients;
        hubClients.DidNotReceiveWithAnyArgs().Group(default!);

        // No cache invalidation published.
        var subscriber = redis.GetSubscriber();
        await subscriber.DidNotReceiveWithAnyArgs().PublishAsync(default, default);
    }

    // ─── IN12: Dashboard deleted → orphan subscriptions removed (FK CASCADE) ─

    [Fact(Skip = "Requires PostgreSQL (Testcontainers) — FK CASCADE verification. Enable in Phase 12.")]
    public Task IN12_DashboardDeleted_OrphanSubscriptionsRemoved()
        => Task.CompletedTask;

    // ─── Multiple widgets per event ──────────────────────────────────────────

    [Fact]
    public async Task IN10b_MultipleWidgets_AllReceiveWidgetStale()
    {
        var (svc, repo, _, _, hubClient) = Build();

        repo.Seed("t1", "order.shipped",
            new EventSubscriptionRow { DashboardCode = "sales",   WidgetId = "orders-chart" },
            new EventSubscriptionRow { DashboardCode = "finance", WidgetId = "revenue-kpi" });

        await svc.ProcessAsync(MakeEvent());

        // hubClient is the shared mock returned by clients.Group() for all groups.
        // Two widgets → WidgetStale called twice (once per widget).
        await hubClient.Received(1).WidgetStale(
            "widget:sales:orders-chart", Arg.Any<WidgetStaleHint>());
        await hubClient.Received(1).WidgetStale(
            "widget:finance:revenue-kpi", Arg.Any<WidgetStaleHint>());
    }
}
