namespace ReportingPlatform.Contracts.Store;

/// <summary>
/// Abstraction over <c>ResultStore</c> for reading completed request results.
/// Placed in Contracts so Adapters can depend on it without referencing Caching directly.
/// </summary>
public interface IResultReader
{
    Task<ResultStoreRecord?> GetAsync(string requestId, CancellationToken ct = default);
}
