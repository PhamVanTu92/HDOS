namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "pie" — maps rows to <see cref="PieData"/>.
/// VisualConfig keys: labelKey, valueKey, valueFormat.
/// </summary>
internal sealed class PieChartTransformer : IWidgetTransformer
{
    public string ChartType => "pie";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
        => Task.FromResult(Build(rows, ctx));

    internal static JsonElement Build(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        WidgetRenderContext ctx)
    {
        var vc          = ctx.VisualConfig();
        var labelKey    = vc.TryGetString("labelKey")    ?? "label";
        var valueKey    = vc.TryGetString("valueKey")    ?? "value";
        var colorKey    = vc.TryGetString("colorKey");
        var valueFormat = vc.TryGetString("valueFormat");

        var slices = rows.Select(r => new PieSlice
        {
            Label = r.GetRowValue(labelKey).ToStringValue() ?? string.Empty,
            Value = r.GetRowValue(valueKey).ToDouble() ?? 0d,
            Color = colorKey is not null ? r.GetRowValue(colorKey).ToStringValue() : null,
        }).ToList();

        var total = slices.Sum(s => s.Value);

        var result = new PieData
        {
            Slices      = slices,
            Total       = total,
            ValueFormat = valueFormat,
        };

        return JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.PieData);
    }
}
