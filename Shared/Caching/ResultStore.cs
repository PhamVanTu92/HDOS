using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.Caching;

// Stores the final serialized response for a completed request. TTL=5min so
// late-joining SSE clients can poll the result without re-executing the operation.
public sealed class ResultStore(IDatabase redis) : IResultReader
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task SetAsync(ResultStoreRecord record, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(record, ResultStoreJsonContext.Default.ResultStoreRecord);
        await redis.StringSetAsync(RedisKeys.Result(record.RequestId), json, Ttl);
    }

    public async Task<ResultStoreRecord?> GetAsync(string requestId, CancellationToken ct = default)
    {
        var value = await redis.StringGetAsync(RedisKeys.Result(requestId));
        if (value.IsNullOrEmpty)
            return null;
        return JsonSerializer.Deserialize(value!, ResultStoreJsonContext.Default.ResultStoreRecord);
    }

    public Task DeleteAsync(string requestId) =>
        redis.KeyDeleteAsync(RedisKeys.Result(requestId));
}

[JsonSerializable(typeof(ResultStoreRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ResultStoreJsonContext : JsonSerializerContext;
