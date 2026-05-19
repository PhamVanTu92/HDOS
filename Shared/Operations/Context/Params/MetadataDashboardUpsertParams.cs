namespace ReportingPlatform.Operations.Context.Params;

public sealed record MetadataDashboardUpsertParams
{
    public required DashboardDefinition Definition { get; init; }
}
