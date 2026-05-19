using ReportingPlatform.Contracts.RenderPayloads.Operations;

namespace ReportingPlatform.Metadata.Abstractions;

public interface IDatasourceMetadataRepository
{
    Task<UpsertResult>                   UpsertAsync(string tenantId, DatasourceDefinition definition, CancellationToken ct = default);
    Task<DeleteResult>                   DeleteAsync(string tenantId, string datasourceId, CancellationToken ct = default);
    Task<DatasourceDefinition?>          GetAsync(string tenantId, string datasourceId, CancellationToken ct = default);
    Task<IReadOnlyList<DatasourceSummary>> ListAsync(string tenantId, CancellationToken ct = default);
}
