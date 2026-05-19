namespace ReportingPlatform.Operations.Context.Params;

public sealed record DatasourcePreviewParams
{
    public required string DatasourceId { get; init; }
    public int?            Limit        { get; init; }
}
