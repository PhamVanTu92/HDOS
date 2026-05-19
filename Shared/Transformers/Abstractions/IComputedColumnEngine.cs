namespace ReportingPlatform.Transformers.Abstractions;

/// <summary>
/// Applies computed column transforms to adapter rows before the widget transformer runs.
/// This is NOT an <see cref="IWidgetTransformer"/> — it has no <c>ChartType</c> and
/// produces augmented rows, not a final <c>JsonElement</c>.
/// </summary>
public interface IComputedColumnEngine
{
    /// <summary>
    /// Returns a new row list with computed columns appended to each row.
    /// The original <paramref name="rows"/> are never mutated.
    /// </summary>
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Apply(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        IReadOnlyList<TableColumn> computedColumns);
}
