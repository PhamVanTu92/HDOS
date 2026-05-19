namespace ReportingPlatform.Metadata.Abstractions;

public interface ISchemaMetadataRepository
{
    Task<UpsertResult>               UpsertAsync(string tenantId, SchemaDefinition definition, CancellationToken ct = default);
    Task<SchemaDefinition?>          GetAsync(string tenantId, string schemaId, CancellationToken ct = default);
    Task<IReadOnlyList<SchemaDefinition>> ListAsync(string tenantId, CancellationToken ct = default);
}
