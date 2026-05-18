using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.Caching;

// Stores which Gateway SignalR connection owns a given requestId so Worker responses
// can be routed back to the right client. TTL=10min (request lifecycle upper bound).
public sealed class OwnerStore(IDatabase redis)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task SetAsync(OwnerStoreRecord record, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(record, OwnerStoreJsonContext.Default.OwnerStoreRecord);
        await redis.StringSetAsync(RedisKeys.Owner(record.RequestId), json, Ttl);
    }

    public async Task<OwnerStoreRecord?> GetAsync(string requestId, CancellationToken ct = default)
    {
        var value = await redis.StringGetAsync(RedisKeys.Owner(requestId));
        if (value.IsNullOrEmpty)
            return null;
        return JsonSerializer.Deserialize(value!, OwnerStoreJsonContext.Default.OwnerStoreRecord);
    }

    public Task DeleteAsync(string requestId) =>
        redis.KeyDeleteAsync(RedisKeys.Owner(requestId));
}

[JsonSerializable(typeof(OwnerStoreRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class OwnerStoreJsonContext : JsonSerializerContext;
