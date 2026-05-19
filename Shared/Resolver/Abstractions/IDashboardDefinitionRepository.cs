namespace ReportingPlatform.Resolver.Abstractions;

/// <summary>
/// Fetches dashboard and datasource definitions from the backing store.
/// Implementations should cache results in-process with a short TTL and
/// evict on Redis pub/sub invalidation events.
/// </summary>
public interface IDashboardDefinitionRepository
{
    /// <summary>
    /// Returns the dashboard definition and its current version string,
    /// or <c>null</c> if the dashboard does not exist for this tenant.
    /// </summary>
    Task<(DashboardDefinition Definition, string Version)?> GetAsync(
        string tenantId,
        string dashboardCode,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches datasource definitions by ID for a tenant.
    /// Returns an empty dictionary for unknown IDs (callers handle missing datasources).
    /// </summary>
    Task<IReadOnlyDictionary<string, DatasourceDefinition>> GetDatasourcesAsync(
        string tenantId,
        IReadOnlyCollection<string> datasourceIds,
        CancellationToken ct = default);
}
