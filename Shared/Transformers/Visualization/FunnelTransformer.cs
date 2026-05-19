namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "funnel" — maps rows to <see cref="FunnelData"/>.
/// VisualConfig keys: labelKey, valueKey.
/// Rows must be pre-sorted in funnel order (first row = widest stage).
/// percentOfStart and dropRate are computed server-side.
/// </summary>
internal sealed class FunnelTransformer : IWidgetTransformer
{
    public string ChartType => "funnel";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc       = ctx.VisualConfig();
        var labelKey = vc.TryGetString("labelKey") ?? "label";
        var valueKey = vc.TryGetString("valueKey") ?? "value";

        var values = rows.Select(r => r.GetRowValue(valueKey).ToLong() ?? 0L).ToArray();
        var start  = values.Length > 0 ? values[0] : 1L;
        if (start == 0L) start = 1L;  // avoid divide-by-zero

        var steps = rows.Select((r, i) =>
        {
            var val = values[i];
            var pct = 100d * val / start;
            var drop = i > 0 && values[i - 1] > 0
                ? 1d - (double)val / values[i - 1]
                : (double?)null;

            return new FunnelStep
            {
                Label          = r.GetRowValue(labelKey).ToStringValue() ?? string.Empty,
                Value          = val,
                PercentOfStart = Math.Round(pct, 2),
                DropRate       = drop.HasValue ? Math.Round(drop.Value, 4) : null,
            };
        }).ToList();

        var result = new FunnelData { Steps = steps };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.FunnelData));
    }
}
