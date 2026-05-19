namespace ReportingPlatform.Transformers.Filter;

/// <summary>
/// chartType: "filter_slider" — produces <see cref="FilterSliderData"/>.
/// VisualConfig keys: filterKey, label, min, max, step, format, rangeMode.
/// </summary>
internal sealed class FilterSliderTransformer : IWidgetTransformer
{
    public string ChartType => "filter_slider";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc        = ctx.VisualConfig();
        var filterKey = vc.TryGetString("filterKey") ?? ctx.Widget.WidgetId;
        var label     = vc.TryGetString("label")     ?? ctx.Widget.Title;
        var min       = vc.TryGetDouble("min")       ?? 0d;
        var max       = vc.TryGetDouble("max")       ?? 100d;
        var step      = vc.TryGetDouble("step")      ?? 1d;
        var format    = vc.TryGetString("format");
        var rangeMode = vc.TryGetBool("rangeMode");

        // Current filter value from active filters
        ctx.Filters.TryGetValue(filterKey, out var currentValue);

        // Default to midpoint when no filter active
        JsonElement effectiveValue;
        if (currentValue.ValueKind != JsonValueKind.Undefined)
        {
            effectiveValue = currentValue;
        }
        else if (rangeMode)
        {
            effectiveValue = JsonDocument.Parse(
                $"{{\"from\":{min},\"to\":{max}}}").RootElement.Clone();
        }
        else
        {
            effectiveValue = JsonSerializer.SerializeToElement(min);
        }

        var result = new FilterSliderData
        {
            FilterKey    = filterKey,
            Label        = label,
            Min          = min,
            Max          = max,
            Step         = step,
            CurrentValue = effectiveValue,
            Format       = format,
            RangeMode    = rangeMode,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.FilterSliderData));
    }
}
