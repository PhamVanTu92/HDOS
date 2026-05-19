namespace ReportingPlatform.Operations.Context.Params;

public sealed record MetadataDatasourceDeleteParams
{
    public required string DatasourceId { get; init; }
}
