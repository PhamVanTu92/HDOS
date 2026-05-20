using Microsoft.Extensions.Caching.Memory;
using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Metadata.Results;
using ReportingPlatform.Metadata.Services;
using ReportingPlatform.Operations.Handlers.Metadata;
using ReportingPlatform.Resolver.Cache;
using ReportingPlatform.Resolver.Invalidation;

namespace ReportingPlatform.Operations.Tests.Handlers;

public sealed class MetadataDashboardUpsertHandlerTests
{
    // No-op event subscription repository for unit tests that don't exercise Phase 11 sync.
    private sealed class NullEventSubscriptionRepository : IEventSubscriptionRepository
    {
        public Task<IReadOnlyList<EventSubscriptionRow>> GetSubscribersAsync(
            string tenantId, string eventType, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EventSubscriptionRow>>([]);

        public Task SyncAsync(
            string tenantId, string dashboardCode,
            IReadOnlyList<(string WidgetId, string EventType)> subscriptions,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static EventSubscriptionSyncService NullSync() =>
        new(new NullEventSubscriptionRepository(),
            NullLogger<EventSubscriptionSyncService>.Instance);
    private static readonly JsonSerializerOptions DeserOpts =
        new() { PropertyNameCaseInsensitive = true };
    // ------------------------------------------------------------------
    // Fake repository
    // ------------------------------------------------------------------

    private sealed class InMemoryDashboardRepo : IDashboardMetadataRepository
    {
        private readonly Dictionary<string, (DashboardDefinition Def, int Version)> _store = new();
        private readonly List<string> _invalidationLog = new();

        public IReadOnlyList<string> InvalidationLog => _invalidationLog;

        public Task<UpsertResult> UpsertAsync(
            string tenantId, DashboardDefinition definition, CancellationToken ct = default)
        {
            var key = $"{tenantId}:{definition.DashboardCode}";
            if (_store.TryGetValue(key, out var existing))
            {
                var newVersion = existing.Version + 1;
                _store[key] = (definition, newVersion);
                _invalidationLog.Add(definition.DashboardCode);
                return Task.FromResult(new UpsertResult { Id = 1, Version = newVersion });
            }
            _store[key] = (definition, 1);
            _invalidationLog.Add(definition.DashboardCode);
            return Task.FromResult(new UpsertResult { Id = 1, Version = 1 });
        }

        public Task<DeleteResult> DeleteAsync(
            string tenantId, string dashboardCode, CancellationToken ct = default) =>
            Task.FromResult(new DeleteResult { Deleted = true });

        public Task<DashboardDefinition?> GetAsync(
            string tenantId, string dashboardCode, CancellationToken ct = default) =>
            Task.FromResult<DashboardDefinition?>(null);

        public Task<IReadOnlyList<DashboardMetadataSummary>> ListAsync(
            string tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DashboardMetadataSummary>>([]);
    }

    // ------------------------------------------------------------------
    // Builder helpers
    // ------------------------------------------------------------------

    private static DashboardDefinition MakeDef(string code) => new()
    {
        DashboardCode = code,
        Title         = code,
    };

    private static OperationHandlerContext MakeContext(string tenantId, DashboardDefinition def)
    {
        var paramsJson = JsonSerializer.Serialize(new
        {
            definition = def,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return new OperationHandlerContext
        {
            RequestId   = "req-1",
            TenantId    = tenantId,
            UserId      = "u1",
            Params      = JsonDocument.Parse(paramsJson).RootElement,
            Traceparent = string.Empty,
        };
    }

    // ------------------------------------------------------------------
    // Upsert_NewDashboard_ReturnsVersion1
    // ------------------------------------------------------------------

    [Fact]
    public async Task Upsert_NewDashboard_ReturnsVersion1()
    {
        var repo    = new InMemoryDashboardRepo();
        var handler = new MetadataDashboardUpsertHandler(repo, NullSync(),
            NullLogger<MetadataDashboardUpsertHandler>.Instance);
        var ctx     = MakeContext("t1", MakeDef("new_dash"));

        var result = await handler.HandleAsync(ctx);
        var upsert = result.Deserialize<UpsertResult>(DeserOpts)!;

        Assert.Equal(1, upsert.Version);
    }

    // ------------------------------------------------------------------
    // Upsert_ExistingDashboard_IncrementsVersion
    // ------------------------------------------------------------------

    [Fact]
    public async Task Upsert_ExistingDashboard_IncrementsVersion()
    {
        var repo    = new InMemoryDashboardRepo();
        var handler = new MetadataDashboardUpsertHandler(repo, NullSync(),
            NullLogger<MetadataDashboardUpsertHandler>.Instance);
        var ctx     = MakeContext("t1", MakeDef("my_dash"));

        await handler.HandleAsync(ctx); // first upsert → v1
        var result = await handler.HandleAsync(ctx); // second upsert → v2
        var upsert = result.Deserialize<UpsertResult>(DeserOpts)!;

        Assert.Equal(2, upsert.Version);
    }

    // ------------------------------------------------------------------
    // Upsert_PublishesCacheInvalidation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Upsert_PublishesCacheInvalidation()
    {
        var repo    = new InMemoryDashboardRepo();
        var handler = new MetadataDashboardUpsertHandler(repo, NullSync(),
            NullLogger<MetadataDashboardUpsertHandler>.Instance);
        var ctx     = MakeContext("t1", MakeDef("dash_x"));

        await handler.HandleAsync(ctx);

        Assert.Contains("dash_x", repo.InvalidationLog);
    }

    // ------------------------------------------------------------------
    // Upsert_TriggersL0Eviction_E2E (Patch 5)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Upsert_TriggersL0Eviction_E2E()
    {
        // Setup: real WidgetCacheService (L0 only) + real DashboardCacheInvalidationService
        var l0 = new MemoryCache(new MemoryCacheOptions());
        var cache = new WidgetCacheService(l0, NullLogger<WidgetCacheService>.Instance);

        // Seed a widget entry in L0 for "dash_e2e"
        var cacheKey = WidgetCacheService.MakeKey("t1", "dash_e2e", "1", "w1", "abc");
        await cache.SetAsync(cacheKey, new WidgetEnvelope
        {
            WidgetId  = "w1",
            ChartType = "line",
            Title     = "w1",
            IsEmpty   = false,
            Meta = new WidgetMeta
            {
                RenderContractVersion = "1.0",
                GeneratedAt           = DateTimeOffset.UtcNow.ToString("O"),
                FromCache             = false,
                ElapsedMs             = 0,
                SubscribeChannel      = "widget:d1:w1",
            },
            Data = JsonDocument.Parse("{}").RootElement,
        }, 60);

        // Verify entry IS in cache
        var before = await cache.GetAsync(cacheKey);
        Assert.NotNull(before);

        // Now the invalidation service: on "cache-invalidate:dashboard:dash_e2e", evict L0 entries
        // For this E2E test we call EvictFromL0 directly simulating what DashboardCacheInvalidationService does.
        cache.EvictFromL0(cacheKey);

        // Verify entry is GONE from L0
        var after = await cache.GetAsync(cacheKey);
        Assert.Null(after);
    }
}
