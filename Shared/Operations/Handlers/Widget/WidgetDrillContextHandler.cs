using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;
using ReportingPlatform.Resolver.Abstractions;

namespace ReportingPlatform.Operations.Handlers.Widget;

internal sealed class WidgetDrillContextHandler : IOperationHandler
{
    public string OperationName => "widget.drillContext";

    private readonly IDashboardDefinitionRepository _definitions;

    public WidgetDrillContextHandler(IDashboardDefinitionRepository definitions) =>
        _definitions = definitions;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<WidgetDrillContextParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "sourceDashboard, widgetId, clickedData, and targetDashboard are required.");

        // Step 1: Load source dashboard
        var defResult = await _definitions.GetAsync(context.TenantId, p.SourceDashboard, ct)
            ?? throw new OperationException("DASHBOARD_NOT_FOUND",
                $"Source dashboard '{p.SourceDashboard}' not found.");

        var (dashboard, _) = defResult;

        // Step 2: Find widget
        var widget = (dashboard.Widgets ?? []).FirstOrDefault(w => w.WidgetId == p.WidgetId)
            ?? throw new OperationException("WIDGET_NOT_FOUND",
                $"Widget '{p.WidgetId}' not found in dashboard '{p.SourceDashboard}'.");

        // Step 3: Read interactionConfig
        if (!widget.InteractionConfig.HasValue ||
            widget.InteractionConfig.Value.ValueKind != JsonValueKind.Object ||
            !widget.InteractionConfig.Value.TryGetProperty("onClickDataPoint", out var onClick))
        {
            return EmptyValid(p.TargetDashboard);
        }

        // Step 4: Read filterMapping
        if (!onClick.TryGetProperty("filterMapping", out var filterMapping) ||
            filterMapping.ValueKind != JsonValueKind.Object)
        {
            return EmptyValid(p.TargetDashboard);
        }

        // Step 5: Verify targetDashboard match
        if (onClick.TryGetProperty("targetDashboardCode", out var targetEl) &&
            targetEl.GetString() is string targetCode &&
            !string.Equals(targetCode, p.TargetDashboard, StringComparison.Ordinal))
        {
            var invalid = new DrillContextResult
            {
                ResolvedFilters     = new Dictionary<string, JsonElement>(),
                TargetDashboardCode = p.TargetDashboard,
                Valid               = false,
            };
            return JsonSerializer.SerializeToElement(invalid,
                OperationsJsonContext.Default.DrillContextResult);
        }

        // Step 6: Resolve token templates
        var resolved = new Dictionary<string, JsonElement>();
        foreach (var prop in filterMapping.EnumerateObject())
        {
            var template = prop.Value.GetString() ?? prop.Value.GetRawText();
            resolved[prop.Name] = ResolveToken(template, p, context.TenantId);
        }

        // Step 7: Return result
        var drillResult = new DrillContextResult
        {
            ResolvedFilters     = resolved,
            TargetDashboardCode = p.TargetDashboard,
            Valid               = true,
        };
        return JsonSerializer.SerializeToElement(drillResult,
            OperationsJsonContext.Default.DrillContextResult);
    }

    private static JsonElement ResolveToken(
        string template,
        WidgetDrillContextParams p,
        string tenantId)
    {
        if (template.StartsWith("{{") && template.EndsWith("}}"))
        {
            var inner = template[2..^2].Trim(); // strip {{ }}

            if (inner.StartsWith("clicked.", StringComparison.Ordinal))
            {
                var field = inner["clicked.".Length..];
                if (p.ClickedData.ValueKind == JsonValueKind.Object &&
                    p.ClickedData.TryGetProperty(field, out var clickedVal))
                    return clickedVal;
                return EmptyString();
            }

            if (inner.StartsWith("filters.", StringComparison.Ordinal))
            {
                var key = inner["filters.".Length..];
                if (p.CurrentFilters.HasValue &&
                    p.CurrentFilters.Value.ValueKind == JsonValueKind.Object &&
                    p.CurrentFilters.Value.TryGetProperty(key, out var filterVal))
                    return filterVal;
                return EmptyString();
            }

            if (string.Equals(inner, "user.tenantId", StringComparison.Ordinal))
            {
                return JsonDocument.Parse($"\"{tenantId}\"").RootElement;
            }

            // Unknown scope — preserve as literal
            return JsonDocument.Parse($"\"{template}\"").RootElement;
        }

        // Literal value
        return JsonDocument.Parse($"\"{template}\"").RootElement;
    }

    private static JsonElement EmptyValid(string targetDashboard)
    {
        var result = new DrillContextResult
        {
            ResolvedFilters     = new Dictionary<string, JsonElement>(),
            TargetDashboardCode = targetDashboard,
            Valid               = true,
        };
        return JsonSerializer.SerializeToElement(result,
            OperationsJsonContext.Default.DrillContextResult);
    }

    private static JsonElement EmptyString() =>
        JsonDocument.Parse("\"\"").RootElement;

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
