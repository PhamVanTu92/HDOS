namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "bar" — maps rows to <see cref="TimeSeriesData"/>.
/// Same VisualConfig contract as <see cref="LineChartTransformer"/>.
/// </summary>
internal sealed class BarChartTransformer : IWidgetTransformer
{
    public string ChartType => "bar";

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

        var result = new TimeSeriesData
        {
            Series = LineChartTransformer.BuildSeries(rows, xKey, yKey, seriesKey),
            Axes = new ChartAxes
            {
                X = new AxisDefinition { Type = xType,    Label = xLabel },
                Y = new AxisDefinition { Type = "number", Label = yLabel },
            },
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.TimeSeriesData));
    }
}
