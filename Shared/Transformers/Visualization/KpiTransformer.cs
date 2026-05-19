namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "kpi" — maps a single-row result to <see cref="KpiData"/>.
/// VisualConfig keys: valueKey, format, label, previousKey (optional),
/// sparklineKey (optional), positiveDirection ("up"|"down", for isGood flag).
/// </summary>
internal sealed class KpiTransformer : IWidgetTransformer
{
    public string ChartType => "kpi";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc           = ctx.VisualConfig();
        var valueKey     = vc.TryGetString("valueKey")     ?? "value";
        var format       = vc.TryGetString("format")       ?? "number";
        var label        = vc.TryGetString("label")        ?? ctx.Widget.Title;
        var previousKey  = vc.TryGetString("previousKey");
        var sparklineKey = vc.TryGetString("sparklineKey");
        var positiveDir  = vc.TryGetString("positiveDirection") ?? "up";

        var firstRow = rows.Count > 0 ? rows[0] : null;
        var value    = firstRow?.GetRowValue(valueKey).ToDouble();

        KpiComparison? comparison = null;
        if (previousKey is not null && firstRow is not null)
        {
            var prev = firstRow.GetRowValue(previousKey).ToDouble();
            if (value is not null && prev is not null)
            {
                var delta   = value.Value - prev.Value;
                var pct     = prev.Value != 0d ? 100d * delta / prev.Value : 0d;
                var dir     = delta > 0d ? "up" : delta < 0d ? "down" : "flat";
                var isGood  = (dir == "up") == (positiveDir == "up");

                comparison = new KpiComparison
                {
                    PreviousValue = prev.Value,
                    Delta         = delta,
                    DeltaPercent  = pct,
                    Direction     = dir,
                    IsGood        = isGood,
                    PeriodLabel   = vc.TryGetString("periodLabel") ?? "vs previous",
                };
            }
        }

        IReadOnlyList<double>? sparkline = null;
        if (sparklineKey is not null && rows.Count > 1)
        {
            sparkline = rows
                .Select(r => r.GetRowValue(sparklineKey).ToDouble() ?? 0d)
                .ToList();
        }

        var result = new KpiData
        {
            Value      = value,
            Format     = format,
            Label      = label,
            Comparison = comparison,
            Sparkline  = sparkline,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.KpiData));
    }
}
