namespace ReportingPlatform.QueryBuilder.Whitelist;

public interface IQueryableSourceRepository
{
    Task<QueryableSource?> GetAsync(string tenantId, string sourceName, CancellationToken ct = default);
}
