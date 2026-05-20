namespace ReportingPlatform.Transformers.Filter;

/// <summary>
/// chartType: "filter_date_range" — produces <see cref="FilterDateRangeData"/>.
/// VisualConfig keys: filterKey, label, defaultFrom, defaultTo, minDate, maxDate,
/// presets (array of {label, value}).
/// </summary>
internal sealed class FilterDateRangeTransformer : IWidgetTransformer
{
    private static readonly IReadOnlyList<DatePreset> DefaultPresets =
    [
        new DatePreset { Label = "Today",        Value = "today"        },
        new DatePreset { Label = "Last 7 days",  Value = "last_7d"      },
        new DatePreset { Label = "This month",   Value = "this_month"   },
        new DatePreset { Label = "This quarter", Value = "this_quarter" },
        new DatePreset { Label = "This year",    Value = "this_year"    },
    ];

    private readonly TimeProvider _clock;

    /// <param name="clock">
    /// Clock used to compute default date range. Defaults to <see cref="TimeProvider.System"/>.
    /// Pass a fixed <see cref="TimeProvider"/> in tests to prevent golden-file drift.
    /// </param>
    public FilterDateRangeTransformer(TimeProvider? clock = null)
        => _clock = clock ?? TimeProvider.System;

    public string ChartType => "filter_date_range";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var now       = _clock.GetUtcNow().DateTime;
        var vc        = ctx.VisualConfig();
        var filterKey = vc.TryGetString("filterKey") ?? ctx.Widget.WidgetId;
        var label     = vc.TryGetString("label")     ?? ctx.Widget.Title;
        var defaultFrom = vc.TryGetString("defaultFrom") ?? now.AddDays(-7).ToString("yyyy-MM-dd");
        var defaultTo   = vc.TryGetString("defaultTo")   ?? now.ToString("yyyy-MM-dd");

        // Current filter value from active filters
        var fromKey = $"{filterKey}_from";
        var toKey   = $"{filterKey}_to";
        var from    = ctx.Filters.TryGetValue(fromKey, out var fe) ? fe.ToStringValue() ?? defaultFrom : defaultFrom;
        var to      = ctx.Filters.TryGetValue(toKey,   out var te) ? te.ToStringValue() ?? defaultTo   : defaultTo;

        var presets = ParsePresets(vc);

        var result = new FilterDateRangeData
        {
            FilterKey    = filterKey,
            Label        = label,
            CurrentValue = new DateRangeValue { From = from, To = to },
            Presets      = presets,
            MinDate      = vc.TryGetString("minDate"),
            MaxDate      = vc.TryGetString("maxDate"),
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.FilterDateRangeData));
    }

    private static IReadOnlyList<DatePreset> ParsePresets(JsonElement vc)
    {
        if (vc.ValueKind == JsonValueKind.Object &&
            vc.TryGetProperty("presets", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            return arr.EnumerateArray().Select(el => new DatePreset
            {
                Label = el.TryGetString("label") ?? string.Empty,
                Value = el.TryGetString("value") ?? string.Empty,
            }).ToList();
        }

        return DefaultPresets;
    }
}
