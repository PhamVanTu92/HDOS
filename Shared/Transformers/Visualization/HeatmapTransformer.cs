namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "heatmap" — maps rows to <see cref="HeatmapData"/>.
/// VisualConfig keys: xKey, yKey, valueKey, colorScale ("sequential"|"diverging"|"categorical").
/// Rows define the sparse cell matrix; X and Y labels are derived from unique values.
/// </summary>
internal sealed class HeatmapTransformer : IWidgetTransformer
{
    public string ChartType => "heatmap";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc         = ctx.VisualConfig();
        var xKey       = vc.TryGetString("xKey")       ?? "x";
        var yKey       = vc.TryGetString("yKey")       ?? "y";
        var valueKey   = vc.TryGetString("valueKey")   ?? "value";
        var colorScale = vc.TryGetString("colorScale") ?? "sequential";

        var xLabels = rows.Select(r => r.GetRowValue(xKey).ToStringValue() ?? string.Empty)
                          .Distinct().Order().ToList();
        var yLabels = rows.Select(r => r.GetRowValue(yKey).ToStringValue() ?? string.Empty)
                          .Distinct().Order().ToList();

        var cells = rows.Select(r =>
        {
            var v = r.GetRowValue(valueKey).ToDouble();
            return new HeatmapCell
            {
                X     = r.GetRowValue(xKey).ToStringValue() ?? string.Empty,
                Y     = r.GetRowValue(yKey).ToStringValue() ?? string.Empty,
                Value = v,
            };
        }).ToList();

        var nonNull = cells.Where(c => c.Value is not null).Select(c => c.Value!.Value).ToList();
        var min     = nonNull.Count > 0 ? nonNull.Min() : 0d;
        var max     = nonNull.Count > 0 ? nonNull.Max() : 0d;

        var result = new HeatmapData
        {
            XLabels    = xLabels,
            YLabels    = yLabels,
            Cells      = cells,
            ValueRange = new HeatmapValueRange { Min = min, Max = max },
            ColorScale = colorScale,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.HeatmapData));
    }
}
