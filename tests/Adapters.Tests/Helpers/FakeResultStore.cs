namespace ReportingPlatform.Adapters.Tests.Helpers;

/// <summary>Seeded in-memory store returned to the adapter after a pub/sub notification.</summary>
internal sealed class FakeResultStore : IResultReader
{
    private readonly Dictionary<string, ResultStoreRecord> _store = new(StringComparer.Ordinal);

    public void Seed(ResultStoreRecord record) => _store[record.RequestId] = record;

    public Task<ResultStoreRecord?> GetAsync(string requestId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(requestId, out var r) ? r : null);
}
