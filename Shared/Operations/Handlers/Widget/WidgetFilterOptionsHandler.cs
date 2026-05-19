using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;
using ReportingPlatform.Operations.Services;
using ReportingPlatform.Resolver.Abstractions;

namespace ReportingPlatform.Operations.Handlers.Widget;

internal sealed class WidgetFilterOptionsHandler : IOperationHandler
{
    public string OperationName => "widget.filterOptions";

    private readonly IDashboardDefinitionRepository _definitions;
    private readonly IDatasourceMetadataRepository _datasources;
    private readonly FilterOptionsService _filterOptions;

    public WidgetFilterOptionsHandler(
        IDashboardDefinitionRepository definitions,
        IDatasourceMetadataRepository datasources,
        FilterOptionsService filterOptions)
    {
        _definitions  = definitions;
        _datasources  = datasources;
        _filterOptions = filterOptions;
    }

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<WidgetFilterOptionsParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "dashboardCode and widgetId are required.");

        var defResult = await _definitions.GetAsync(context.TenantId, p.DashboardCode, ct)
            ?? throw new OperationException("DASHBOARD_NOT_FOUND",
                $"Dashboard '{p.DashboardCode}' not found.");

        var (dashboard, _) = defResult;
        var widget = (dashboard.Widgets ?? []).FirstOrDefault(w => w.WidgetId == p.WidgetId)
            ?? throw new OperationException("WIDGET_NOT_FOUND",
                $"Widget '{p.WidgetId}' not found in dashboard '{p.DashboardCode}'.");

        // Check for static options first (no adapter call needed)
        if (widget.VisualConfig.HasValue &&
            widget.VisualConfig.Value.ValueKind == JsonValueKind.Object &&
            widget.VisualConfig.Value.TryGetProperty("staticOptions", out var staticEl) &&
            staticEl.ValueKind == JsonValueKind.Array)
        {
            var staticOptions = staticEl.EnumerateArray()
                .Select(el => new FilterOption
                {
                    Value = el.TryGetProperty("value", out var v) ? v.GetString() ?? v.GetRawText() : "",
                    Label = el.TryGetProperty("label", out var l) ? l.GetString() ?? l.GetRawText() : "",
                })
                .Where(o => string.IsNullOrEmpty(p.Search) ||
                            o.Label.Contains(p.Search, StringComparison.OrdinalIgnoreCase) ||
                            o.Value.Contains(p.Search, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var staticResult = new FilterOptionsResult
            {
                FilterKey = p.WidgetId,
                Options   = staticOptions,
                HasMore   = false,
            };
            return JsonSerializer.SerializeToElement(staticResult,
                OperationsJsonContext.Default.FilterOptionsResult);
        }

        // Dynamic options from adapter
        var datasource = await _datasources.GetAsync(context.TenantId, widget.DatasourceId, ct)
            ?? throw new OperationException("DATASOURCE_NOT_FOUND",
                $"Datasource '{widget.DatasourceId}' not found.");

        var options = await _filterOptions.FetchAsync(
            context.TenantId, widget, datasource,
            new Dictionary<string, JsonElement>(), p.Search, ct);

        var result = new FilterOptionsResult
        {
            FilterKey = p.WidgetId,
            Options   = options,
            HasMore   = false,
        };
        return JsonSerializer.SerializeToElement(result,
            OperationsJsonContext.Default.FilterOptionsResult);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
