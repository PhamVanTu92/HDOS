namespace ReportingPlatform.RequestApi.Services;

/// <summary>
/// Redis-backed failure counter and lockout state for provider token endpoint.
/// Rate limit: 10 req/min per clientId. Lockout: 5 failures in 60s → 5-min lockout.
/// </summary>
public sealed class ProviderLockoutService
{
    private readonly IDatabase _redis;

    private const int FailureTtlSeconds  = 60;
    private const int LockoutTtlSeconds  = 300;
    private const int MaxFailures        = 5;
    private const int RateLimitPerMinute = 10;

    public ProviderLockoutService(IDatabase redis) => _redis = redis;

    public async Task<bool> IsLockedOutAsync(string clientId, CancellationToken ct = default)
    {
        _ = ct;
        return await _redis.KeyExistsAsync(LockoutKey(clientId));
    }

    public async Task<bool> IsRateLimitedAsync(string clientId, CancellationToken ct = default)
    {
        _ = ct;
        var key   = RateKey(clientId);
        var count = await _redis.StringIncrementAsync(key);
        if (count == 1)
            await _redis.KeyExpireAsync(key, TimeSpan.FromSeconds(60));
        return count > RateLimitPerMinute;
    }

    public async Task<bool> RecordFailureAsync(string clientId, CancellationToken ct = default)
    {
        _ = ct;
        var failKey = FailureKey(clientId);
        var count   = await _redis.StringIncrementAsync(failKey);
        if (count == 1)
            await _redis.KeyExpireAsync(failKey, TimeSpan.FromSeconds(FailureTtlSeconds));

        if (count >= MaxFailures)
        {
            await _redis.StringSetAsync(LockoutKey(clientId), "1",
                TimeSpan.FromSeconds(LockoutTtlSeconds));
            return true;
        }
        return false;
    }

    public async Task ClearFailuresAsync(string clientId, CancellationToken ct = default)
    {
        _ = ct;
        await _redis.KeyDeleteAsync(FailureKey(clientId));
    }

    private static string LockoutKey(string clientId) => $"rp:auth:locked:{clientId}";
    private static string FailureKey(string clientId)  => $"rp:auth:failures:{clientId}";
    private static string RateKey(string clientId)     => $"rp:auth:rate:cid:{clientId}";
}
