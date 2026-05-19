using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.QueryBuilder.Builder;
using ReportingPlatform.QueryBuilder.Whitelist;
using ReportingPlatform.Resolver.Tests.Helpers;
using ReportingPlatform.Transformers.Filter;
using ReportingPlatform.Transformers.Layout;
using ReportingPlatform.Transformers.Table;
using ReportingPlatform.Transformers.Visualization;

namespace ReportingPlatform.Resolver.Tests.Validation;

/// <summary>
/// Tests for the nine validator rules (R1–R9).
/// Uses a real <see cref="TransformerRegistry"/> so R1 can check known chart types.
/// </summary>
public sealed class WidgetDefinitionValidatorTests
{
    // ------------------------------------------------------------------
    // Shared infrastructure
    // ------------------------------------------------------------------

    private static WidgetDefinitionValidator MakeValidator(
        IQueryableSourceRepository? sources = null) =>
        new(TransformerRegistryFactory.Create(),
            sources ?? new FakeSourceRepo());

    private static DatasourceDefinition SqlDs(string id, string mode = "raw") => new()
    {
        DatasourceId     = id,
        DisplayName      = id,
        Type             = "sql",
        ConnectionConfig = JsonSerializer.SerializeToElement(new { mode }),
    };

    private static WidgetDefinition Widget(
        string id, string chartType, string datasourceId = "ds1",
        JsonElement? visualConfig = null) => new()
    {
        WidgetId      = id,
        ChartType     = chartType,
        Title         = id,
        DatasourceId  = datasourceId,
        VisualConfig  = visualConfig,
    };

    private static DashboardDefinition Dashboard(params WidgetDefinition[] widgets) => new()
    {
        DashboardCode = "test",
        Title         = "Test Dashboard",
        Widgets       = widgets,
    };

    private static Dictionary<string, DatasourceDefinition> Datasources(
        params DatasourceDefinition[] defs) =>
        defs.ToDictionary(d => d.DatasourceId);

    // ------------------------------------------------------------------
    // R1: unknown chartType
    // ------------------------------------------------------------------

    [Fact]
    public async Task R1_UnknownChartType_Fails()
    {
        var validator = MakeValidator();
        var result    = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "not_a_real_chart")),
            Datasources());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "UNKNOWN_CHART_TYPE");
    }

    [Fact]
    public async Task R1_KnownChartType_Passes()
    {
        var validator = MakeValidator();
        var result    = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "line")),
            Datasources(SqlDs("ds1")));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    // ------------------------------------------------------------------
    // R4: advanced_table + timescale → PAGINATION_NOT_SUPPORTED
    // ------------------------------------------------------------------

    [Fact]
    public async Task R4_AdvancedTable_TimescaleSource_Fails()
    {
        var validator = MakeValidator();
        var result    = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "advanced_table", "ds_ts")),
            Datasources(SqlDs("ds_ts", "timescale")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PAGINATION_NOT_SUPPORTED");
    }

    [Fact]
    public async Task R4_AdvancedTable_RawSource_Passes()
    {
        var validator = MakeValidator();
        var result    = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "advanced_table", "ds_raw")),
            Datasources(SqlDs("ds_raw", "raw")));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    // ------------------------------------------------------------------
    // R5: timeoutMs range [1000, 300000]
    // ------------------------------------------------------------------

    [Fact]
    public async Task R5_TimeoutMs_TooLow_Fails()
    {
        var vc = JsonSerializer.SerializeToElement(new { timeoutMs = 500 });
        var validator = MakeValidator();
        var result = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "line", visualConfig: vc)),
            Datasources(SqlDs("ds1")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_TIMEOUT");
    }

    [Fact]
    public async Task R5_TimeoutMs_Valid_Passes()
    {
        var vc = JsonSerializer.SerializeToElement(new { timeoutMs = 5000 });
        var validator = MakeValidator();
        var result = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "line", visualConfig: vc)),
            Datasources(SqlDs("ds1")));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    // ------------------------------------------------------------------
    // R6: gauge requires min + max, and max > min
    // ------------------------------------------------------------------

    [Fact]
    public async Task R6_Gauge_MissingMin_Fails()
    {
        var vc = JsonSerializer.SerializeToElement(new { max = 100 });
        var validator = MakeValidator();
        var result = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "gauge", visualConfig: vc)),
            Datasources(SqlDs("ds1")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_GAUGE_CONFIG");
    }

    [Fact]
    public async Task R6_Gauge_MaxNotGreaterThanMin_Fails()
    {
        var vc = JsonSerializer.SerializeToElement(new { min = 100, max = 50 });
        var validator = MakeValidator();
        var result = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "gauge", visualConfig: vc)),
            Datasources(SqlDs("ds1")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_GAUGE_CONFIG");
    }

    [Fact]
    public async Task R6_Gauge_ValidConfig_Passes()
    {
        var vc = JsonSerializer.SerializeToElement(new { min = 0, max = 100 });
        var validator = MakeValidator();
        var result = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "gauge", visualConfig: vc)),
            Datasources(SqlDs("ds1")));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    // ------------------------------------------------------------------
    // R9: duplicate widget IDs
    // ------------------------------------------------------------------

    [Fact]
    public async Task R9_DuplicateWidgetId_Fails()
    {
        var validator = MakeValidator();
        var result    = await validator.ValidateAsync(
            "t1",
            Dashboard(
                Widget("dup", "line"),
                Widget("dup", "bar")),
            Datasources(SqlDs("ds1")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DUPLICATE_WIDGET_ID");
    }

    [Fact]
    public async Task R9_UniqueWidgetIds_Passes()
    {
        var validator = MakeValidator();
        var result    = await validator.ValidateAsync(
            "t1",
            Dashboard(Widget("w1", "line"), Widget("w2", "bar")),
            Datasources(SqlDs("ds1")));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    // ------------------------------------------------------------------
    // Multiple errors collected (not fail-fast)
    // ------------------------------------------------------------------

    [Fact]
    public async Task MultipleErrors_AllCollected()
    {
        var validator = MakeValidator();
        // Two widgets with: unknown chartType + duplicate ID
        var result = await validator.ValidateAsync(
            "t1",
            Dashboard(
                Widget("same_id", "unknown_type"),
                Widget("same_id", "also_unknown")),
            Datasources());

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3, // 1 duplicate + 2 unknown types
            $"Expected ≥3 errors, got {result.Errors.Count}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Fake IQueryableSourceRepository — always returns null (source not found).</summary>
    private sealed class FakeSourceRepo : IQueryableSourceRepository
    {
        public Task<QueryableSource?> GetAsync(
            string tenantId, string sourceName, CancellationToken ct = default) =>
            Task.FromResult<QueryableSource?>(null);

        public Task<IReadOnlyList<QueryableSource>> GetAllAsync(
            string tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QueryableSource>>([]);
    }

    /// <summary>Builds a real TransformerRegistry for R1 checks.</summary>
    private static class TransformerRegistryFactory
    {
        public static TransformerRegistry Create() => new(
        [
            // Visualization (10)
            new LineChartTransformer(),
            new BarChartTransformer(),
            new AreaChartTransformer(),
            new PieChartTransformer(),
            new DonutChartTransformer(),
            new KpiTransformer(),
            new GaugeTransformer(),
            new HeatmapTransformer(),
            new ScatterTransformer(),
            new FunnelTransformer(),
            // Table (3)
            new SimpleTableTransformer(),
            new AdvancedTableTransformer(),
            new PivotTableTransformer(),
            // Filter (4)
            new FilterDropdownTransformer(),
            new FilterDateRangeTransformer(),
            new FilterSliderTransformer(),
            new FilterSearchTransformer(),
            // Layout (2)
            new TextWidgetTransformer(),
            new TabContainerTransformer(),
        ]);
    }
}
