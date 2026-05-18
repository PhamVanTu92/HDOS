using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.Caching;

// Deduplicates identical in-flight or recently-completed requests within the same tenant.
// The caller computes the operationKey (e.g. SHA256 of operation+params).
public sealed class IdempotencyStore(IDatabase redis)
{
    // Attempt to claim the idempotency slot. Returns true if this call is the first (owner);
    // false if another request already holds the slot.
    public async Task<bool> TryClaimAsync(
        string tenantId,
        string operationKey,
        string requestId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var record = new IdempotencyRecord
        {
            RequestId    = requestId,
            OperationKey = operationKey,
            Status       = IdempotencyStatus.Processing,
            CreatedAt    = DateTimeOffset.UtcNow.ToString("O"),
        };
        var json = JsonSerializer.Serialize(record, IdempotencyJsonContext.Default.IdempotencyRecord);
        // SET NX — only sets if key does not exist.
        return await redis.StringSetAsync(
            RedisKeys.Idempotency(tenantId, operationKey), json, ttl, When.NotExists);
    }

    public async Task<IdempotencyRecord?> GetAsync(
        string tenantId,
        string operationKey,
        CancellationToken ct = default)
    {
        var value = await redis.StringGetAsync(RedisKeys.Idempotency(tenantId, operationKey));
        if (value.IsNullOrEmpty)
            return null;
        return JsonSerializer.Deserialize(value!, IdempotencyJsonContext.Default.IdempotencyRecord);
    }

    public async Task MarkCompletedAsync(
        string tenantId,
        string operationKey,
        string requestId,
        CancellationToken ct = default)
    {
        var existing = await GetAsync(tenantId, operationKey, ct);
        if (existing is null || existing.RequestId != requestId)
            return;

        var updated = existing with { Status = IdempotencyStatus.Completed };
        var json    = JsonSerializer.Serialize(updated, IdempotencyJsonContext.Default.IdempotencyRecord);
        var remainingTtl = await redis.KeyTimeToLiveAsync(RedisKeys.Idempotency(tenantId, operationKey));
        if (remainingTtl.HasValue)
            await redis.StringSetAsync(RedisKeys.Idempotency(tenantId, operationKey), json, remainingTtl.Value);
    }
}

[JsonSerializable(typeof(IdempotencyRecord))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
internal partial class IdempotencyJsonContext : JsonSerializerContext;
