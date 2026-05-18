namespace ReportingPlatform.Contracts.Definitions;

public sealed record DatasourceDefinition
{
    public required string DatasourceId { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }

    // "sql" | "rest_api" | "grpc" | "static"
    public required string Type { get; init; }

    public required JsonElement ConnectionConfig { get; init; }
    public int? CacheSeconds { get; init; }
    public int? MaxRows { get; init; }
}
