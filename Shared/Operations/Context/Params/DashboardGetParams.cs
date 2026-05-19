namespace ReportingPlatform.Operations.Context.Params;

public sealed record DashboardGetParams
{
    public required string DashboardCode { get; init; }
}
