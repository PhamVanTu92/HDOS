namespace ReportingPlatform.Adapters.Models;

public sealed record AdapterResult
{
    public required IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows { get; init; }

    // Non-null for paginated sources; null for aggregated/chart queries.
    public long? TotalRows { get; init; }

    // Column schema — may be null for adapters that don't introspect metadata.
    public IReadOnlyList<ColumnDescriptor>? Schema { get; init; }
}
