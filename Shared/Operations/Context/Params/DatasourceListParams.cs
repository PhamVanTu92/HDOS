namespace ReportingPlatform.Operations.Context.Params;

public sealed record DatasourceListParams
{
    public string? TenantId { get; init; }
}
