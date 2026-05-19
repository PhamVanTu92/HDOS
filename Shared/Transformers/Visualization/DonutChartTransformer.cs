namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "donut" — maps rows to <see cref="PieData"/> (same structure as pie).
/// VisualConfig keys: same as <see cref="PieChartTransformer"/>.
/// </summary>
internal sealed class DonutChartTransformer : IWidgetTransformer
{
    public string ChartType => "donut";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
        => Task.FromResult(PieChartTransformer.Build(rows, ctx));
}
