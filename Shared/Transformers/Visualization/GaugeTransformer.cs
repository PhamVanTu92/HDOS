namespace ReportingPlatform.Transformers.Visualization;

/// <summary>
/// chartType: "gauge" — maps a single-value result to <see cref="GaugeData"/>.
/// VisualConfig keys: valueKey, min, max, unit, target,
/// thresholds (array of {from, to, color, label}).
/// </summary>
internal sealed class GaugeTransformer : IWidgetTransformer
{
    public string ChartType => "gauge";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc       = ctx.VisualConfig();
        var valueKey = vc.TryGetString("valueKey") ?? "value";
        var min      = vc.TryGetDouble("min") ?? 0d;
        var max      = vc.TryGetDouble("max") ?? 100d;
        var unit     = vc.TryGetString("unit");
        var target   = vc.TryGetDouble("target");

        var value = rows.Count > 0
            ? rows[0].GetRowValue(valueKey).ToDouble() ?? 0d
            : 0d;

        var thresholds = ParseThresholds(vc);

        var result = new GaugeData
        {
            Value      = value,
            Min        = min,
            Max        = max,
            Unit       = unit,
            Target     = target,
            Thresholds = thresholds,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.GaugeData));
    }

    private static IReadOnlyList<GaugeThreshold> ParseThresholds(JsonElement vc)
    {
        if (vc.ValueKind != JsonValueKind.Object ||
            !vc.TryGetProperty("thresholds", out var t) ||
            t.ValueKind != JsonValueKind.Array)
        {
            // Default: green 0-80, yellow 80-90, red 90-100
            return
            [
                new GaugeThreshold { From = 0d,  To = 80d,  Color = "#22c55e", Label = "OK"      },
                new GaugeThreshold { From = 80d, To = 90d,  Color = "#eab308", Label = "Warning" },
                new GaugeThreshold { From = 90d, To = 100d, Color = "#ef4444", Label = "Critical" },
            ];
        }

        return t.EnumerateArray().Select(el => new GaugeThreshold
        {
            From  = el.TryGetDouble("from")  ?? 0d,
            To    = el.TryGetDouble("to")    ?? 100d,
            Color = el.TryGetString("color") ?? "#94a3b8",
            Label = el.TryGetString("label") ?? string.Empty,
        }).ToList();
    }
}
