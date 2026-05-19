namespace ReportingPlatform.QueryBuilder.Whitelist;

public sealed record QueryableSource
{
    public required string TenantId { get; init; }
    public required string SourceName { get; init; }
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    // Empty list = all columns allowed
    public required IReadOnlyList<string> AllowedColumns { get; init; }
    // Empty list = falls back to AllowedColumns for sort validation
    public required IReadOnlyList<string> SortableColumns { get; init; }
    public int MaxRows { get; init; } = 10_000;
}
