namespace ReportingPlatform.Contracts.RenderPayloads.Operations;

public sealed record DatasourceListPayload
{
    public required IReadOnlyList<DatasourceSummary> Datasources { get; init; }
}

public sealed record DatasourceSummary
{
    public required string DatasourceId { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string Type { get; init; }
}
