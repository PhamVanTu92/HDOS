namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "scatter" — maps rows to <see cref="ScatterData"/>.
/// VisualConfig keys: xKey, yKey, sizeKey (optional), labelKey (optional),
/// colorKey (optional), seriesKey (optional grouping), xLabel, yLabel.
/// </summary>
internal sealed class ScatterTransformer : IWidgetTransformer
{
    public string ChartType => "scatter";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc        = ctx.VisualConfig();
        var xKey      = vc.TryGetString("xKey")      ?? "x";
        var yKey      = vc.TryGetString("yKey")      ?? "y";
        var sizeKey   = vc.TryGetString("sizeKey");
        var labelKey  = vc.TryGetString("labelKey");
        var colorKey  = vc.TryGetString("colorKey");
        var seriesKey = vc.TryGetString("seriesKey");
        var xLabel    = vc.TryGetString("xLabel")    ?? xKey;
        var yLabel    = vc.TryGetString("yLabel")    ?? yKey;

        var series = rows
            .GroupBy(r => seriesKey is not null
                ? r.GetRowValue(seriesKey).ToStringValue() ?? "(other)"
                : "(all)")
            .Select(g => new ScatterSeries
            {
                Name   = g.Key,
                Points = g.Select(r => new ScatterPoint
                {
                    X     = r.GetRowValue(xKey).ToDouble() ?? 0d,
                    Y     = r.GetRowValue(yKey).ToDouble() ?? 0d,
                    Size  = sizeKey  is not null ? r.GetRowValue(sizeKey).ToDouble()  : null,
                    Label = labelKey is not null ? r.GetRowValue(labelKey).ToStringValue() : null,
                    Color = colorKey is not null ? r.GetRowValue(colorKey).ToStringValue() : null,
                }).ToList(),
            })
            .ToList();

        var result = new ScatterData
        {
            Series = series,
            Axes = new ChartAxes
            {
                X = new AxisDefinition { Type = "number", Label = xLabel },
                Y = new AxisDefinition { Type = "number", Label = yLabel },
            },
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.ScatterData));
    }
}
