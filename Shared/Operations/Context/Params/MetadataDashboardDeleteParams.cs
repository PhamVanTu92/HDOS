namespace ReportingPlatform.Operations.Context.Params;

public sealed record MetadataDashboardDeleteParams
{
    public required string DashboardCode { get; init; }
}
