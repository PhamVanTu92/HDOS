namespace ReportingPlatform.Operations.Context.Params;

public sealed record MetadataDatasourceUpsertParams
{
    public required DatasourceDefinition Definition { get; init; }
}
