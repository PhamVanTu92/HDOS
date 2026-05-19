namespace ReportingPlatform.Metadata.Abstractions;

public interface IDashboardMetadataRepository
{
    Task<UpsertResult>                     UpsertAsync(string tenantId, DashboardDefinition definition, CancellationToken ct = default);
    Task<DeleteResult>                     DeleteAsync(string tenantId, string dashboardCode, CancellationToken ct = default);
    Task<DashboardDefinition?>             GetAsync(string tenantId, string dashboardCode, CancellationToken ct = default);
    Task<IReadOnlyList<DashboardMetadataSummary>> ListAsync(string tenantId, CancellationToken ct = default);
}
