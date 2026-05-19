namespace ReportingPlatform.Transformers.Layout;

/// <summary>
/// chartType: "tab_container" — reads tab definitions from VisualConfig, no adapter call.
/// VisualConfig["tabs"]: [{id, label, widgetIds: string[], default?: bool}]
///
/// Tab child widgets are declared as top-level widgets in DashboardDefinition and rendered
/// in the same fan-out pass. The resolver does NOT recursively render tab children here.
/// </summary>
internal sealed class TabContainerTransformer : IWidgetTransformer
{
    public string ChartType => "tab_container";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc   = ctx.VisualConfig();
        var tabs = ParseTabs(vc);

        var result = new TabContainerData { Tabs = tabs };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.TabContainerData));
    }

    private static IReadOnlyList<TabDefinition> ParseTabs(JsonElement vc)
    {
        if (vc.ValueKind != JsonValueKind.Object ||
            !vc.TryGetProperty("tabs", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray().Select(el => new TabDefinition
        {
            Id        = el.TryGetString("id")    ?? Guid.NewGuid().ToString("N"),
            Label     = el.TryGetString("label") ?? "Tab",
            WidgetIds = el.ValueKind == JsonValueKind.Object &&
                        el.TryGetProperty("widgetIds", out var ids) &&
                        ids.ValueKind == JsonValueKind.Array
                            ? ids.EnumerateArray()
                                 .Select(e => e.GetString() ?? string.Empty)
                                 .ToList()
                            : [],
            Default   = el.TryGetBool("default"),
        }).ToList();
    }
}
