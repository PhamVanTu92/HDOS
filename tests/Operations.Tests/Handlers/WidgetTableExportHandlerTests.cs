using System.Text;
using ReportingPlatform.Adapters.Abstractions;
using ReportingPlatform.Adapters.Models;
using ReportingPlatform.Operations.Handlers.Widget;

namespace ReportingPlatform.Operations.Tests.Handlers;

public sealed class WidgetTableExportHandlerTests
{
    private static readonly JsonSerializerOptions DeserOpts =
        new() { PropertyNameCaseInsensitive = true };
    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class FakeDashboardRepo : IDashboardDefinitionRepository
    {
        private readonly DashboardDefinition _dashboard;
        private readonly DatasourceDefinition _datasource;

        public FakeDashboardRepo(DashboardDefinition dashboard, DatasourceDefinition datasource)
        {
            _dashboard  = dashboard;
            _datasource = datasource;
        }

        public Task<(DashboardDefinition Definition, string Version)?> GetAsync(
            string tenantId, string dashboardCode, CancellationToken ct = default) =>
            Task.FromResult((_dashboard, "1") as (DashboardDefinition, string)?);

        public Task<IReadOnlyDictionary<string, DatasourceDefinition>> GetDatasourcesAsync(
            string tenantId, IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DatasourceDefinition>>(
                new Dictionary<string, DatasourceDefinition> { [_datasource.DatasourceId] = _datasource });
    }

    private sealed class FakeAdapterFactory : IDatasourceAdapterFactory
    {
        private readonly IDatasourceAdapter _adapter;
        public FakeAdapterFactory(IDatasourceAdapter adapter) => _adapter = adapter;
        public IDatasourceAdapter Resolve(DatasourceDefinition definition) => _adapter;
    }

    private sealed class StaticRowsAdapter : IDatasourceAdapter
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> _rows;
        public StaticRowsAdapter(IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows) =>
            _rows = rows;

        public Task<AdapterResult> FetchAsync(AdapterRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AdapterResult { Rows = _rows, TotalRows = _rows.Count });
    }

    // ------------------------------------------------------------------
    // Builder
    // ------------------------------------------------------------------

    private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> MakeRows(int count)
    {
        var rows = new List<IReadOnlyDictionary<string, JsonElement>>();
        for (var i = 0; i < count; i++)
        {
            rows.Add(new Dictionary<string, JsonElement>
            {
                ["id"]   = JsonDocument.Parse(i.ToString()).RootElement,
                ["name"] = JsonDocument.Parse($"\"item{i}\"").RootElement,
            });
        }
        return rows;
    }

    private static OperationHandlerContext MakeContext(string widgetId, string format, string dashCode = "d1") =>
        new()
        {
            RequestId   = "req-1",
            TenantId    = "t1",
            UserId      = "u1",
            Traceparent = string.Empty,
            Params      = JsonDocument.Parse(
                $"{{\"dashboardCode\":\"{dashCode}\",\"widgetId\":\"{widgetId}\",\"format\":\"{format}\"}}").RootElement,
        };

    private static WidgetTableExportHandler MakeHandler(int rowCount)
    {
        var rows      = MakeRows(rowCount);
        var adapter   = new StaticRowsAdapter(rows);
        var widget    = new WidgetDefinition { WidgetId = "w1", ChartType = "simple_table", Title = "w1", DatasourceId = "ds1" };
        var datasource = new DatasourceDefinition
        {
            DatasourceId     = "ds1",
            DisplayName      = "DS",
            Type             = "sql",
            ConnectionConfig = JsonDocument.Parse("{}").RootElement,
        };
        var dashboard = new DashboardDefinition
        {
            DashboardCode = "d1",
            Title         = "D1",
            Widgets       = [widget],
        };
        var repo     = new FakeDashboardRepo(dashboard, datasource);
        var factory  = new FakeAdapterFactory(adapter);
        return new WidgetTableExportHandler(repo, factory);
    }

    // ------------------------------------------------------------------
    // Export_SmallCsv_ReturnsBase64
    // ------------------------------------------------------------------

    [Fact]
    public async Task Export_SmallCsv_ReturnsBase64()
    {
        var handler = MakeHandler(10);
        var result  = await handler.HandleAsync(MakeContext("w1", "csv"));
        var export  = result.Deserialize<TableExportResult>(DeserOpts)!;

        Assert.Equal("csv", export.Format);
        Assert.NotNull(export.ContentBase64);
        Assert.True(export.SizeBytes > 0);

        // Decode and verify it's valid CSV
        var csv = Encoding.UTF8.GetString(Convert.FromBase64String(export.ContentBase64!));
        Assert.Contains("id", csv);
        Assert.Contains("name", csv);
    }

    // ------------------------------------------------------------------
    // Export_SmallXlsx_ReturnsBase64
    // ------------------------------------------------------------------

    [Fact]
    public async Task Export_SmallXlsx_ReturnsBase64()
    {
        var handler = MakeHandler(10);
        var result  = await handler.HandleAsync(MakeContext("w1", "xlsx"));
        var export  = result.Deserialize<TableExportResult>(DeserOpts)!;

        Assert.Equal("xlsx", export.Format);
        Assert.NotNull(export.ContentBase64);
        Assert.True(export.SizeBytes > 0);

        // XLSX magic bytes: PK\x03\x04 (ZIP)
        var bytes = Convert.FromBase64String(export.ContentBase64!);
        Assert.Equal(0x50, bytes[0]); // 'P'
        Assert.Equal(0x4B, bytes[1]); // 'K'
    }

    // ------------------------------------------------------------------
    // Export_LargeDataset_ThrowsNotSupported
    // ------------------------------------------------------------------

    [Fact]
    public async Task Export_LargeDataset_ThrowsNotSupported()
    {
        var handler = MakeHandler(5_001);

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            handler.HandleAsync(MakeContext("w1", "csv")));

        Assert.Equal("LARGE_EXPORT_NOT_SUPPORTED", ex.Code);
    }

    // ------------------------------------------------------------------
    // Export_UnknownFormat_ThrowsInvalidParams
    // ------------------------------------------------------------------

    [Fact]
    public async Task Export_UnknownFormat_ThrowsInvalidParams()
    {
        var handler = MakeHandler(10);

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            handler.HandleAsync(MakeContext("w1", "pdf")));

        Assert.Equal("INVALID_PARAMS", ex.Code);
    }
}
