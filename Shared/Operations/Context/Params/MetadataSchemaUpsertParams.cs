namespace ReportingPlatform.Operations.Context.Params;

public sealed record MetadataSchemaUpsertParams
{
    public required SchemaDefinition Definition { get; init; }
}
