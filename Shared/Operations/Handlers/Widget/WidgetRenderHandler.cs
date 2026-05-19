using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;
using ReportingPlatform.Resolver.Abstractions;

namespace ReportingPlatform.Operations.Handlers.Widget;

internal sealed class WidgetRenderHandler : IOperationHandler
{
    public string OperationName => "widget.render";

    private readonly IDashboardResolver _resolver;

    public WidgetRenderHandler(IDashboardResolver resolver) => _resolver = resolver;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<WidgetRenderParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "dashboardCode and widgetId are required.");

        if (string.IsNullOrWhiteSpace(p.DashboardCode))
            throw new OperationException("INVALID_PARAMS", "dashboardCode must be non-empty.");
        if (string.IsNullOrWhiteSpace(p.WidgetId))
            throw new OperationException("INVALID_PARAMS", "widgetId must be non-empty.");

        var filters = p.Filters ?? new Dictionary<string, JsonElement>();

        // Render full dashboard then extract the single widget envelope
        var payload = await _resolver.RenderAsync(
            context.TenantId, p.DashboardCode, filters, p.TableParams, ct);

        var envelope = payload.Widgets.FirstOrDefault(w => w.WidgetId == p.WidgetId)
            ?? throw new OperationException("WIDGET_NOT_FOUND",
                $"Widget '{p.WidgetId}' not found in dashboard '{p.DashboardCode}'.");

        return JsonSerializer.SerializeToElement(payload,
            OperationsJsonContext.Default.DashboardRenderPayload);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
