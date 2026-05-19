namespace ReportingPlatform.Transformers.Filter;

/// <summary>
/// chartType: "filter_dropdown" — produces <see cref="FilterDropdownData"/>.
///
/// Option sources (evaluated in order):
///  1. Pre-fetched options from <see cref="WidgetRenderContext.DropdownOptions"/>
///     (keyed by widgetId), populated by the resolver's pre-fetch phase when
///     the widget has an <c>optionsSource</c> in VisualConfig.
///  2. Static options from VisualConfig["staticOptions"] — each element {value, label, count?}.
///
/// The transformer NEVER performs adapter calls (all DB I/O is done in the pre-fetch phase).
/// </summary>
internal sealed class FilterDropdownTransformer : IWidgetTransformer
{
    public string ChartType => "filter_dropdown";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc        = ctx.VisualConfig();
        var filterKey = vc.TryGetString("filterKey") ?? ctx.Widget.WidgetId;
        var label     = vc.TryGetString("label")     ?? ctx.Widget.Title;
        var multi     = vc.TryGetBool("multiSelect");
        var searchable= vc.TryGetBool("searchable");
        var clearable = vc.TryGetBool("clearable", true);

        // Current filter value from active filters
        ctx.Filters.TryGetValue(filterKey, out var currentValue);

        // Resolve options: pre-fetched > static > inferred from rows
        var options = ResolveOptions(ctx, vc, rows);

        var result = new FilterDropdownData
        {
            FilterKey    = filterKey,
            Label        = label,
            Options      = options,
            CurrentValue = currentValue.ValueKind != JsonValueKind.Undefined
                ? currentValue
                : null,
            MultiSelect  = multi,
            Searchable   = searchable,
            Clearable    = clearable,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.FilterDropdownData));
    }

    private static IReadOnlyList<FilterOption> ResolveOptions(
        WidgetRenderContext ctx,
        JsonElement vc,
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows)
    {
        // 1. Pre-fetched options from resolver pre-fetch phase
        if (ctx.DropdownOptions is not null &&
            ctx.DropdownOptions.TryGetValue(ctx.Widget.WidgetId, out var prefetched))
            return prefetched;

        // 2. Static options from VisualConfig
        if (vc.ValueKind == JsonValueKind.Object &&
            vc.TryGetProperty("staticOptions", out var staticArr) &&
            staticArr.ValueKind == JsonValueKind.Array)
        {
            return staticArr.EnumerateArray().Select(el => new FilterOption
            {
                Value = el.TryGetString("value") ?? string.Empty,
                Label = el.TryGetString("label") ?? el.TryGetString("value") ?? string.Empty,
                Count = el.ValueKind == JsonValueKind.Object &&
                        el.TryGetProperty("count", out var c) &&
                        c.TryGetInt64(out var cv) ? cv : null,
            }).ToList();
        }

        // 3. Infer from adapter rows (valueKey, labelKey)
        var valueKey = vc.TryGetString("valueKey") ?? "value";
        var labelKey = vc.TryGetString("labelKey") ?? valueKey;
        return rows.Select(r => new FilterOption
        {
            Value = r.GetRowValue(valueKey).ToStringValue() ?? string.Empty,
            Label = r.GetRowValue(labelKey).ToStringValue() ?? string.Empty,
        }).ToList();
    }
}
