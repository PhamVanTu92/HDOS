namespace ReportingPlatform.Transformers.Registry;

/// <summary>
/// Maps chartType strings to their <see cref="IWidgetTransformer"/> implementations.
/// Populated once at startup from all registered transformers.
/// </summary>
public sealed class TransformerRegistry
{
    private readonly IReadOnlyDictionary<string, IWidgetTransformer> _map;

    public TransformerRegistry(IEnumerable<IWidgetTransformer> transformers)
    {
        var map = new Dictionary<string, IWidgetTransformer>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in transformers)
        {
            if (!map.TryAdd(t.ChartType, t))
                throw new InvalidOperationException(
                    $"Duplicate transformer for chartType '{t.ChartType}'.");
        }
        _map = map;
    }

    /// <summary>
    /// All chartTypes supported by registered transformers.
    /// </summary>
    public IReadOnlyCollection<string> SupportedTypes => _map.Keys.ToList();

    /// <summary>
    /// Resolves the transformer for <paramref name="chartType"/>.
    /// Returns null if no transformer is registered for that type.
    /// </summary>
    public IWidgetTransformer? Resolve(string chartType) =>
        _map.TryGetValue(chartType, out var t) ? t : null;
}
