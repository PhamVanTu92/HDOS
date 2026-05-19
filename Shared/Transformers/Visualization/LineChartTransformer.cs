namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "line" — maps rows to <see cref="TimeSeriesData"/>.
/// VisualConfig keys: xKey, yKey, xType ("category"|"time"|"number"),
/// xLabel, yLabel, seriesKey (optional grouping column).
/// </summary>
internal sealed class LineChartTransformer : IWidgetTransformer
{
    public string ChartType => "line";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc       = ctx.VisualConfig();
        var xKey     = vc.TryGetString("xKey") ?? "x";
        var yKey     = vc.TryGetString("yKey") ?? "y";
        var xType    = vc.TryGetString("xType") ?? "category";
        var xLabel   = vc.TryGetString("xLabel") ?? xKey;
        var yLabel   = vc.TryGetString("yLabel") ?? yKey;
        var seriesKey = vc.TryGetString("seriesKey");

        var series = BuildSeries(rows, xKey, yKey, seriesKey);

        var result = new TimeSeriesData
        {
            Series = series,
            Axes = new ChartAxes
            {
                X = new AxisDefinition { Type = xType,    Label = xLabel },
                Y = new AxisDefinition { Type = "number", Label = yLabel },
            },
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.TimeSeriesData));
    }

    internal static IReadOnlyList<ChartSeries> BuildSeries(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        string xKey, string yKey, string? seriesKey)
    {
        if (seriesKey is null)
        {
            var points = rows.Select(r => new SeriesPoint
            {
                X = r.GetRowValue(xKey),
                Y = r.GetRowValue(yKey).ToDouble(),
            }).ToList();
            return [new ChartSeries { Name = yKey, Data = points }];
        }

        // Group by series key
        return rows
            .GroupBy(r => r.GetRowValue(seriesKey).ToStringValue() ?? "(other)")
            .Select(g => new ChartSeries
            {
                Name = g.Key,
                Data = g.Select(r => new SeriesPoint
                {
                    X = r.GetRowValue(xKey),
                    Y = r.GetRowValue(yKey).ToDouble(),
                }).ToList(),
            })
            .ToList();
    }
}
