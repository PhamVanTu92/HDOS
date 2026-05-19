using ReportingPlatform.Adapters.Abstractions;
using ReportingPlatform.Adapters.Models;
using ReportingPlatform.Contracts.RenderPayloads.Operations;
using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Handlers.Widget;
using ReportingPlatform.Operations.Services;

namespace ReportingPlatform.Operations.Tests.Handlers;

public sealed class WidgetFilterOptionsHandlerTests
{
    private static readonly JsonSerializerOptions DeserOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class FakeDashboardRepo : IDashboardDefinitionRepository
    {
        private readonly DashboardDefinition? _dashboard;
        public FakeDashboardRepo(DashboardDefinition? dashboard = null) => _dashboard = dashboard;

        public Task<(DashboardDefinition Definition, string Version)?> GetAsync(
            string tenantId, string dashboardCode, CancellationToken ct = default) =>
            Task.FromResult(_dashboard is not null
                ? ((_dashboard, "1") as (DashboardDefinition, string)?)
                : null);

        public Task<IReadOnlyDictionary<string, DatasourceDefinition>> GetDatasourcesAsync(
            string tenantId, IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DatasourceDefinition>>(
                new Dictionary<string, DatasourceDefinition>());
    }

    private sealed class FakeDatasourceMetaRepo : IDatasourceMetadataRepository
    {
        private readonly DatasourceDefinition? _ds;
        public FakeDatasourceMetaRepo(DatasourceDefinition? ds = null) => _ds = ds;

        public Task<DatasourceDefinition?> GetAsync(
            string tenantId, string datasourceId, CancellationToken ct = default) =>
            Task.FromResult(_ds);

        public Task<UpsertResult> UpsertAsync(
            string tenantId, DatasourceDefinition definition, CancellationToken ct = default) =>
            Task.FromResult(new UpsertResult { Id = 1, Version = 1 });

        public Task<DeleteResult> DeleteAsync(
            string tenantId, string datasourceId, CancellationToken ct = default) =>
            Task.FromResult(new DeleteResult { Deleted = true });

        public Task<IReadOnlyList<DatasourceSummary>> ListAsync(
            string tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DatasourceSummary>>([]);
    }

    private sealed class StaticRowsAdapter : IDatasourceAdapter
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> _rows;
        public StaticRowsAdapter(IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows) =>
            _rows = rows;

        public Task<AdapterResult> FetchAsync(AdapterRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AdapterResult { Rows = _rows, TotalRows = _rows.Count });
    }

    private sealed class FakeAdapterFactory : IDatasourceAdapterFactory
    {
        private readonly IDatasourceAdapter _adapter;
        public FakeAdapterFactory(IDatasourceAdapter adapter) => _adapter = adapter;
        public IDatasourceAdapter Resolve(DatasourceDefinition definition) => _adapter;
    }

    // ------------------------------------------------------------------
    // Builder helpers
    // ------------------------------------------------------------------

    private static DatasourceDefinition MakeDatasource(string id = "ds1") => new()
    {
        DatasourceId     = id,
        DisplayName      = "DS",
        Type             = "sql",
        ConnectionConfig = JsonDocument.Parse("{}").RootElement,
    };

    private static WidgetDefinition WidgetWithStaticOptions(
        string widgetId,
        IEnumerable<(string value, string label)> options)
    {
        var optJson = "[" + string.Join(",",
            options.Select(o => $"{{\"value\":\"{o.value}\",\"label\":\"{o.label}\"}}")) + "]";
        var visualJson = $"{{\"staticOptions\":{optJson}}}";
        return new WidgetDefinition
        {
            WidgetId     = widgetId,
            ChartType    = "filter_dropdown",
            Title        = widgetId,
            DatasourceId = "ds1",
            VisualConfig = JsonDocument.Parse(visualJson).RootElement,
        };
    }

    private static WidgetDefinition WidgetWithAdapterSource(
        string widgetId, string sourceName, string valueKey = "id", string labelKey = "name")
    {
        var visualJson = $"{{\"optionsSource\":{{\"source\":\"{sourceName}\",\"valueKey\":\"{valueKey}\",\"labelKey\":\"{labelKey}\"}}}}";
        return new WidgetDefinition
        {
            WidgetId     = widgetId,
            ChartType    = "filter_dropdown",
            Title        = widgetId,
            DatasourceId = "ds1",
            VisualConfig = JsonDocument.Parse(visualJson).RootElement,
        };
    }

    private static DashboardDefinition DashboardWith(WidgetDefinition widget) => new()
    {
        DashboardCode = "dash1",
        Title         = "Dash",
        Widgets       = [widget],
    };

    private static OperationHandlerContext MakeContext(
        string widgetId, string? search = null, string dashCode = "dash1") =>
        new()
        {
            RequestId   = "req-1",
            TenantId    = "t1",
            UserId      = "u1",
            Traceparent = string.Empty,
            Params      = JsonDocument.Parse(search is null
                ? $"{{\"dashboardCode\":\"{dashCode}\",\"widgetId\":\"{widgetId}\"}}"
                : $"{{\"dashboardCode\":\"{dashCode}\",\"widgetId\":\"{widgetId}\",\"search\":\"{search}\"}}").RootElement,
        };

    private static WidgetFilterOptionsHandler MakeHandler(
        DashboardDefinition dashboard,
        DatasourceDefinition? datasource = null,
        IDatasourceAdapter? adapter = null)
    {
        var ds       = datasource ?? MakeDatasource();
        var factory  = new FakeAdapterFactory(adapter ?? new StaticRowsAdapter([]));
        var svc      = new FilterOptionsService(factory, NullLogger<FilterOptionsService>.Instance);
        return new WidgetFilterOptionsHandler(
            new FakeDashboardRepo(dashboard),
            new FakeDatasourceMetaRepo(ds),
            svc);
    }

    // ------------------------------------------------------------------
    // FilterOptions_StaticOptions_ReturnsFromVisualConfig
    // ------------------------------------------------------------------

    [Fact]
    public async Task FilterOptions_StaticOptions_ReturnsFromVisualConfig()
    {
        var widget  = WidgetWithStaticOptions("f1", [("us", "United States"), ("ca", "Canada"), ("uk", "United Kingdom")]);
        var handler = MakeHandler(DashboardWith(widget));

        var result  = await handler.HandleAsync(MakeContext("f1"));
        var opts    = result.Deserialize<FilterOptionsResult>(DeserOpts)!;

        Assert.Equal("f1", opts.FilterKey);
        Assert.Equal(3, opts.Options.Count);
        Assert.Contains(opts.Options, o => o.Value == "us" && o.Label == "United States");
        Assert.Contains(opts.Options, o => o.Value == "ca" && o.Label == "Canada");
    }

    // ------------------------------------------------------------------
    // FilterOptions_AdapterSource_FetchesFromAdapter
    // ------------------------------------------------------------------

    [Fact]
    public async Task FilterOptions_AdapterSource_FetchesFromAdapter()
    {
        var rows = new List<IReadOnlyDictionary<string, JsonElement>>
        {
            new Dictionary<string, JsonElement>
            {
                ["id"]   = JsonDocument.Parse("\"apac\"").RootElement,
                ["name"] = JsonDocument.Parse("\"Asia Pacific\"").RootElement,
            },
            new Dictionary<string, JsonElement>
            {
                ["id"]   = JsonDocument.Parse("\"emea\"").RootElement,
                ["name"] = JsonDocument.Parse("\"Europe Middle East Africa\"").RootElement,
            },
        };

        var widget  = WidgetWithAdapterSource("region", "regions_table", valueKey: "id", labelKey: "name");
        var handler = MakeHandler(DashboardWith(widget), adapter: new StaticRowsAdapter(rows));

        var result = await handler.HandleAsync(MakeContext("region"));
        var opts   = result.Deserialize<FilterOptionsResult>(DeserOpts)!;

        Assert.Equal("region", opts.FilterKey);
        Assert.Equal(2, opts.Options.Count);
        Assert.Contains(opts.Options, o => o.Value == "apac" && o.Label == "Asia Pacific");
        Assert.Contains(opts.Options, o => o.Value == "emea" && o.Label == "Europe Middle East Africa");
    }

    // ------------------------------------------------------------------
    // FilterOptions_StaticWithSearch_FiltersResults
    // ------------------------------------------------------------------

    [Fact]
    public async Task FilterOptions_StaticWithSearch_FiltersResults()
    {
        var widget  = WidgetWithStaticOptions("country", [
            ("us", "United States"), ("gb", "Great Britain"), ("de", "Germany")]);
        var handler = MakeHandler(DashboardWith(widget));

        var result = await handler.HandleAsync(MakeContext("country", search: "great"));
        var opts   = result.Deserialize<FilterOptionsResult>(DeserOpts)!;

        Assert.Single(opts.Options);
        Assert.Equal("gb", opts.Options[0].Value);
        Assert.Equal("Great Britain", opts.Options[0].Label);
    }
}
