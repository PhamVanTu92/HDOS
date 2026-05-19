namespace ReportingPlatform.Transformers.Abstractions;

public interface IWidgetTransformer
{
    /// <summary>
    /// The <c>chartType</c> value from <see cref="WidgetDefinition.ChartType"/>
    /// that this transformer handles. Must be unique across all registered transformers.
    /// </summary>
    string ChartType { get; }

    /// <summary>
    /// Converts adapter rows into the widget-specific JSON payload.
    /// The returned <see cref="JsonElement"/> is placed directly into
    /// <c>WidgetEnvelope.Data</c> — no further serialization occurs.
    /// </summary>
    Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext context,
        CancellationToken ct = default);
}
