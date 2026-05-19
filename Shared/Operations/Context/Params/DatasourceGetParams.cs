namespace ReportingPlatform.Operations.Context.Params;

public sealed record DatasourceGetParams
{
    public required string DatasourceId { get; init; }
}
