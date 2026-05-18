namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record PivotCell
{
    public required IReadOnlyList<string> RowKey { get; init; }
    public required IReadOnlyList<string> ColumnKey { get; init; }
    public required IReadOnlyDictionary<string, double?> Values { get; init; }
}
