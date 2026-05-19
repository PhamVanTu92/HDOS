namespace ReportingPlatform.Transformers.Filter;

/// <summary>
/// chartType: "filter_search" — produces <see cref="FilterSearchData"/>.
/// VisualConfig keys: filterKey, label, placeholder.
/// </summary>
internal sealed class FilterSearchTransformer : IWidgetTransformer
{
    public string ChartType => "filter_search";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc        = ctx.VisualConfig();
        var filterKey = vc.TryGetString("filterKey")  ?? ctx.Widget.WidgetId;
        var label     = vc.TryGetString("label")      ?? ctx.Widget.Title;
        var placeholder = vc.TryGetString("placeholder");

        var currentValue = ctx.Filters.TryGetValue(filterKey, out var fv)
            ? fv.ToStringValue() ?? string.Empty
            : string.Empty;

        var result = new FilterSearchData
        {
            FilterKey    = filterKey,
            Label        = label,
            CurrentValue = currentValue,
            Placeholder  = placeholder,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.FilterSearchData));
    }
}
