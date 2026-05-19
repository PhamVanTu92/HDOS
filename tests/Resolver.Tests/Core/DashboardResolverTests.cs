using ReportingPlatform.Resolver.Tests.Helpers;
using ReportingPlatform.Transformers.Engine;
using ReportingPlatform.Transformers.Filter;
using ReportingPlatform.Transformers.Layout;
using ReportingPlatform.Transformers.Table;
using ReportingPlatform.Transformers.Visualization;

namespace ReportingPlatform.Resolver.Tests.Core;

public sealed class DashboardResolverTests
{
    // ------------------------------------------------------------------
    // Builder helpers
    // ------------------------------------------------------------------

    private static DashboardResolver MakeResolver(
        IDashboardDefinitionRepository repo,
        IDatasourceAdapterFactory? adapterFactory = null,
        WidgetCacheService? cache = null,
        int maxConcurrent = 10) =>
        new(
            repo,
            adapterFactory ?? new FakeAdapterFactory(FakeAdapter.Empty()),
            MakeRegistry(),
            new ComputedColumnEngine(),
            cache ?? MakeCache(),
            Microsoft.Extensions.Options.Options.Create(new ResolverOptions
            {
                MaxConcurrentWidgets      = maxConcurrent,
                DefaultWidgetTimeoutMs    = 30_000,
            }),
            NullLogger<DashboardResolver>.Instance);

    private static TransformerRegistry MakeRegistry() => new(
    [
        new LineChartTransformer(),
        new BarChartTransformer(),
        new SimpleTableTransformer(),
        new AdvancedTableTransformer(),
        new FilterDropdownTransformer(),
        new TextWidgetTransformer(),
        new TabContainerTransformer(),
        new KpiTransformer(),
        new GaugeTransformer(),
        new PieChartTransformer(),
        new DonutChartTransformer(),
        new AreaChartTransformer(),
        new HeatmapTransformer(),
        new ScatterTransformer(),
        new FunnelTransformer(),
        new PivotTableTransformer(),
        new FilterDateRangeTransformer(),
        new FilterSliderTransformer(),
        new FilterSearchTransformer(),
    ]);

    // L0-only cache (no Redis) — uses internal test constructor
    private static WidgetCacheService MakeCache() =>
        new(new MemoryCache(new MemoryCacheOptions()),
            NullLogger<WidgetCacheService>.Instance);

    private static DashboardDefinition SimpleDashboard(
        string code, params WidgetDefinition[] widgets) => new()
    {
        DashboardCode = code,
        Title         = code,
        Widgets       = widgets,
    };

    private static WidgetDefinition LineWidget(string id, string datasourceId = "ds1") =>
        new()
        {
            WidgetId     = id,
            ChartType    = "line",
            Title        = id,
            DatasourceId = datasourceId,
        };

    private static DatasourceDefinition RawDs(string id) => new()
    {
        DatasourceId     = id,
        DisplayName      = id,
        Type             = "sql",
        CacheSeconds     = 0,  // no caching → fresh adapter call every time
        ConnectionConfig = JsonSerializer.SerializeToElement(new { mode = "raw" }),
    };

    private static readonly IReadOnlyDictionary<string, JsonElement> NoFilters =
        new Dictionary<string, JsonElement>();

    // ------------------------------------------------------------------
    // CacheHit_SkipsAdapter
    // ------------------------------------------------------------------

    [Fact]
    public async Task CacheHit_SkipsAdapter()
    {
        // Arrange: pre-seed a widget envelope in the cache
        var cache = MakeCache();
        var repo  = new FakeDashboardRepo(
            SimpleDashboard("d1", LineWidget("w1")),
            version: "1",
            datasources: new() { ["ds1"] = RawDs("ds1") with { CacheSeconds = 60 } });

        // Pre-compute the expected cache key
        var canonical  = FilterCanonicalizer.Canonicalize(NoFilters);
        var hash       = FilterCanonicalizer.Hash(canonical);
        var cacheKey   = WidgetCacheService.MakeKey("t1", "d1", "1", "w1", hash);

        var cachedEnvelope = new WidgetEnvelope
        {
            WidgetId  = "w1",
            ChartType = "line",
            Title     = "w1",
            IsEmpty   = false,
            Meta = new WidgetMeta
            {
                RenderContractVersion = "1.0",
                GeneratedAt           = DateTimeOffset.UtcNow.ToString("O"),
                FromCache             = true,
                ElapsedMs             = 0,
                SubscribeChannel      = "widget:d1:w1",
            },
            Data = JsonSerializer.SerializeToElement(new { cached = true }),
        };
        await cache.SetAsync(cacheKey, cachedEnvelope, 60);

        // A throwing adapter — if the cache is hit, this should never be called
        var factory = new FakeAdapterFactory(FakeAdapter.Throwing("SHOULD_NOT_BE_CALLED", "adapter was invoked"));
        var resolver = MakeResolver(repo, factory, cache);

        // Act
        var payload = await resolver.RenderAsync("t1", "d1", NoFilters);

        // Assert: the envelope came from cache (contains our sentinel property)
        var w1 = Assert.Single(payload.Widgets, e => e.WidgetId == "w1");
        Assert.Null(w1.Error);
        Assert.True(w1.Data.TryGetProperty("cached", out var cv) && cv.GetBoolean());
    }

    // ------------------------------------------------------------------
    // VersionBump_InvalidatesCache
    // ------------------------------------------------------------------

    [Fact]
    public async Task VersionBump_InvalidatesCache()
    {
        // Arrange: seed cache with version "1" key
        var cache = MakeCache();

        var hashV1  = FilterCanonicalizer.Hash(FilterCanonicalizer.Canonicalize(NoFilters));
        var keyV1   = WidgetCacheService.MakeKey("t1", "d1", "1", "w1", hashV1);

        var oldEnvelope = new WidgetEnvelope
        {
            WidgetId  = "w1",
            ChartType = "line",
            Title     = "w1",
            IsEmpty   = false,
            Meta = new WidgetMeta
            {
                RenderContractVersion = "1.0",
                GeneratedAt           = DateTimeOffset.UtcNow.ToString("O"),
                FromCache             = true,
                ElapsedMs             = 0,
                SubscribeChannel      = "widget:d1:w1",
            },
            Data = JsonSerializer.SerializeToElement(new { stale = true }),
        };
        await cache.SetAsync(keyV1, oldEnvelope, 60);

        // Now the repo returns version "2" — different cache key
        var repo = new FakeDashboardRepo(
            SimpleDashboard("d1", LineWidget("w1")),
            version: "2",
            datasources: new() { ["ds1"] = RawDs("ds1") });

        var resolver = MakeResolver(repo, cache: cache);

        // Act
        var payload = await resolver.RenderAsync("t1", "d1", NoFilters);

        // Assert: old stale cache entry not used — fresh render, no "stale" property
        var w1 = Assert.Single(payload.Widgets, e => e.WidgetId == "w1");
        Assert.Null(w1.Error);
        Assert.False(w1.Data.TryGetProperty("stale", out _),
            "Expected fresh render, but got stale cached envelope");
    }

    // ------------------------------------------------------------------
    // WidgetFailure_OtherWidgetsComplete
    // ------------------------------------------------------------------

    [Fact]
    public async Task WidgetFailure_OtherWidgetsComplete()
    {
        // Arrange: two widgets, one per datasource
        // ds_fail → adapter throws; ds_ok → adapter returns empty rows
        const string FailWidgetId = "w_fail";
        const string OkWidgetId   = "w_ok";

        var dashboard = SimpleDashboard("d1",
            LineWidget(FailWidgetId, "ds_fail"),
            LineWidget(OkWidgetId,   "ds_ok"));

        var repo = new FakeDashboardRepo(
            dashboard,
            version: "1",
            datasources: new()
            {
                ["ds_fail"] = RawDs("ds_fail"),
                ["ds_ok"]   = RawDs("ds_ok"),
            });

        // Route by datasourceId
        var failAdapter = FakeAdapter.Throwing("DS_ERROR", "simulated failure");
        var okAdapter   = FakeAdapter.Empty();

        var routingFactory = new RoutingAdapterFactory(new Dictionary<string, IDatasourceAdapter>
        {
            ["ds_fail"] = failAdapter,
            ["ds_ok"]   = okAdapter,
        });

        var resolver = MakeResolver(repo, routingFactory);

        // Act
        var payload = await resolver.RenderAsync("t1", "d1", NoFilters);

        // Assert: two envelopes returned
        Assert.Equal(2, payload.Widgets.Count);

        var failing = Assert.Single(payload.Widgets, e => e.WidgetId == FailWidgetId);
        var ok      = Assert.Single(payload.Widgets, e => e.WidgetId == OkWidgetId);

        // Failing widget has error, not null
        Assert.NotNull(failing.Error);
        Assert.Equal("DS_ERROR", failing.Error!.Code);

        // OK widget has no error
        Assert.Null(ok.Error);
    }

    // ------------------------------------------------------------------
    // Per-datasource routing adapter factory (used only in isolation test)
    // ------------------------------------------------------------------

    private sealed class RoutingAdapterFactory : IDatasourceAdapterFactory
    {
        private readonly Dictionary<string, IDatasourceAdapter> _map;

        public RoutingAdapterFactory(Dictionary<string, IDatasourceAdapter> map) =>
            _map = map;

        public IDatasourceAdapter Resolve(DatasourceDefinition definition) =>
            _map.TryGetValue(definition.DatasourceId, out var a)
                ? a
                : FakeAdapter.Empty();
    }
}
