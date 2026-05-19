using ReportingPlatform.Operations.Handlers.Widget;

namespace ReportingPlatform.Operations.Tests.Handlers;

public sealed class WidgetDrillContextHandlerTests
{
    private static readonly JsonSerializerOptions DeserOpts =
        new() { PropertyNameCaseInsensitive = true };
    // ------------------------------------------------------------------
    // Fake repository
    // ------------------------------------------------------------------

    private sealed class FakeDashboardRepo : IDashboardDefinitionRepository
    {
        private readonly DashboardDefinition? _dashboard;

        public FakeDashboardRepo(DashboardDefinition? dashboard = null) =>
            _dashboard = dashboard;

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

    // ------------------------------------------------------------------
    // Builder helpers
    // ------------------------------------------------------------------

    private static DashboardDefinition DashboardWith(WidgetDefinition widget) => new()
    {
        DashboardCode = "src",
        Title         = "Source",
        Widgets       = [widget],
    };

    private static WidgetDefinition LineWidgetWithMapping(
        string widgetId,
        string targetCode,
        Dictionary<string, string> filterMapping)
    {
        var mappingJson = "{" + string.Join(",",
            filterMapping.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) + "}";
        var interactionJson = $"{{\"onClickDataPoint\":{{\"targetDashboardCode\":\"{targetCode}\",\"filterMapping\":{mappingJson}}}}}";

        return new WidgetDefinition
        {
            WidgetId          = widgetId,
            ChartType         = "line",
            Title             = widgetId,
            DatasourceId      = "ds1",
            InteractionConfig = JsonDocument.Parse(interactionJson).RootElement,
        };
    }

    private static OperationHandlerContext MakeContext(
        string sourceDash, string widgetId,
        object clickedData, string targetDash,
        object? currentFilters = null,
        string tenantId = "t1")
    {
        var clickedJson = JsonSerializer.Serialize(clickedData);
        var paramsObj = new Dictionary<string, object?>
        {
            ["sourceDashboard"] = sourceDash,
            ["widgetId"]        = widgetId,
            ["clickedData"]     = JsonDocument.Parse(clickedJson).RootElement,
            ["targetDashboard"] = targetDash,
        };
        if (currentFilters is not null)
        {
            var filtersJson = JsonSerializer.Serialize(currentFilters);
            paramsObj["currentFilters"] = JsonDocument.Parse(filtersJson).RootElement;
        }

        var paramsJson = JsonSerializer.Serialize(paramsObj);
        return new OperationHandlerContext
        {
            RequestId   = "req-1",
            TenantId    = tenantId,
            UserId      = "u1",
            Params      = JsonDocument.Parse(paramsJson).RootElement,
            Traceparent = string.Empty,
        };
    }

    private static WidgetDrillContextHandler MakeHandler(DashboardDefinition? dashboard = null) =>
        new(new FakeDashboardRepo(dashboard));

    // ------------------------------------------------------------------
    // DrillContext_ClickedToken_Resolved
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_ClickedToken_Resolved()
    {
        var widget = LineWidgetWithMapping("w1", "detail", new() { ["region"] = "{{clicked.region}}" });
        var handler = MakeHandler(DashboardWith(widget));
        var ctx = MakeContext("src", "w1",
            clickedData: new { region = "north" },
            targetDash: "detail");

        var result = await handler.HandleAsync(ctx);
        var drill  = result.Deserialize<DrillContextResult>(DeserOpts)!;

        Assert.True(drill.Valid);
        Assert.Equal("north", drill.ResolvedFilters["region"].GetString());
    }

    // ------------------------------------------------------------------
    // DrillContext_FiltersToken_Resolved
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_FiltersToken_Resolved()
    {
        var widget = LineWidgetWithMapping("w1", "detail", new() { ["year"] = "{{filters.year}}" });
        var handler = MakeHandler(DashboardWith(widget));
        var ctx = MakeContext("src", "w1",
            clickedData: new { x = "jan" },
            targetDash: "detail",
            currentFilters: new { year = 2025 });

        var result = await handler.HandleAsync(ctx);
        var drill  = result.Deserialize<DrillContextResult>(DeserOpts)!;

        Assert.True(drill.Valid);
        Assert.Equal(2025, drill.ResolvedFilters["year"].GetInt32());
    }

    // ------------------------------------------------------------------
    // DrillContext_UserTenantIdToken_Resolved
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_UserTenantIdToken_Resolved()
    {
        var widget = LineWidgetWithMapping("w1", "detail", new() { ["tenant"] = "{{user.tenantId}}" });
        var handler = MakeHandler(DashboardWith(widget));
        var ctx = MakeContext("src", "w1",
            clickedData: new { x = 1 },
            targetDash: "detail",
            tenantId: "acme");

        var result = await handler.HandleAsync(ctx);
        var drill  = result.Deserialize<DrillContextResult>(DeserOpts)!;

        Assert.True(drill.Valid);
        Assert.Equal("acme", drill.ResolvedFilters["tenant"].GetString());
    }

    // ------------------------------------------------------------------
    // DrillContext_LiteralValue_PassedThrough
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_LiteralValue_PassedThrough()
    {
        var widget = LineWidgetWithMapping("w1", "detail", new() { ["region"] = "north" });
        var handler = MakeHandler(DashboardWith(widget));
        var ctx = MakeContext("src", "w1",
            clickedData: new { x = 1 },
            targetDash: "detail");

        var result = await handler.HandleAsync(ctx);
        var drill  = result.Deserialize<DrillContextResult>(DeserOpts)!;

        Assert.True(drill.Valid);
        Assert.Equal("north", drill.ResolvedFilters["region"].GetString());
    }

    // ------------------------------------------------------------------
    // DrillContext_UnknownWidget_Throws
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_UnknownWidget_Throws()
    {
        var widget  = LineWidgetWithMapping("w1", "detail", new());
        var handler = MakeHandler(DashboardWith(widget));
        var ctx     = MakeContext("src", "NONEXISTENT", clickedData: new { }, targetDash: "detail");

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            handler.HandleAsync(ctx));

        Assert.Equal("WIDGET_NOT_FOUND", ex.Code);
    }

    // ------------------------------------------------------------------
    // DrillContext_TargetMismatch_ReturnsInvalid
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_TargetMismatch_ReturnsInvalid()
    {
        var widget  = LineWidgetWithMapping("w1", "correct_target", new() { ["k"] = "v" });
        var handler = MakeHandler(DashboardWith(widget));
        var ctx     = MakeContext("src", "w1", clickedData: new { }, targetDash: "wrong_target");

        var result = await handler.HandleAsync(ctx);
        var drill  = result.Deserialize<DrillContextResult>(DeserOpts)!;

        Assert.False(drill.Valid);
    }

    // ------------------------------------------------------------------
    // DrillContext_NoFilterMapping_ReturnsEmptyValid
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_NoFilterMapping_ReturnsEmptyValid()
    {
        // Widget with no interactionConfig
        var widget = new WidgetDefinition
        {
            WidgetId     = "w1",
            ChartType    = "line",
            Title        = "w1",
            DatasourceId = "ds1",
        };
        var handler = MakeHandler(DashboardWith(widget));
        var ctx     = MakeContext("src", "w1", clickedData: new { }, targetDash: "detail");

        var result = await handler.HandleAsync(ctx);
        var drill  = result.Deserialize<DrillContextResult>(DeserOpts)!;

        Assert.True(drill.Valid);
        Assert.Empty(drill.ResolvedFilters);
    }

    // ------------------------------------------------------------------
    // DrillContext_MissingClickedField_ResolvesEmpty
    // ------------------------------------------------------------------

    [Fact]
    public async Task DrillContext_MissingClickedField_ResolvesEmpty()
    {
        var widget  = LineWidgetWithMapping("w1", "detail", new() { ["x"] = "{{clicked.MISSING}}" });
        var handler = MakeHandler(DashboardWith(widget));
        var ctx     = MakeContext("src", "w1", clickedData: new { y = 1 }, targetDash: "detail");

        var result = await handler.HandleAsync(ctx);
        var drill  = result.Deserialize<DrillContextResult>(DeserOpts)!;

        Assert.True(drill.Valid);
        Assert.Equal(string.Empty, drill.ResolvedFilters["x"].GetString());
    }
}
