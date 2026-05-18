namespace ReportingPlatform.Caching;

// Prevents duplicate concurrent cache-miss executions for the same cache key.
// The first caller acquires a Redis lock and executes the factory; subsequent callers
// poll until the result is available or the lock expires.
public sealed class SingleFlightCoordinator(IDatabase redis)
{
    private static readonly TimeSpan LockTtl  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(200);

    // Returns (result, wasOwner). wasOwner=true means this call executed the factory.
    public async Task<(string? Result, bool WasOwner)> ExecuteAsync(
        string cacheKey,
        Func<Task<string?>> factory,
        CancellationToken ct = default)
    {
        var lockKey    = RedisKeys.SingleFlightLock(cacheKey);
        var resultKey  = RedisKeys.WidgetCache("_sflight", cacheKey);
        var lockToken  = Guid.CreateVersion7().ToString("N");

        // Try to acquire the lock.
        var acquired = await redis.StringSetAsync(lockKey, lockToken, LockTtl, When.NotExists);

        if (acquired)
        {
            try
            {
                var result = await factory();
                if (result is not null)
                    await redis.StringSetAsync(resultKey, result, LockTtl);
                return (result, true);
            }
            finally
            {
                // Release lock only if we still own it.
                var script = LuaScript.Prepare(
                    "if redis.call('GET', @key) == @token then return redis.call('DEL', @key) else return 0 end");
                await redis.ScriptEvaluateAsync(script, new { key = (RedisKey)lockKey, token = lockToken });
            }
        }

        // Not the owner — poll for the result.
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(PollDelay, ct);

            var cached = await redis.StringGetAsync(resultKey);
            if (!cached.IsNullOrEmpty)
                return (cached!, false);

            // Lock gone and no result means the owner failed — return null so the caller can handle.
            if (!await redis.KeyExistsAsync(lockKey))
                return (null, false);
        }

        ct.ThrowIfCancellationRequested();
        return (null, false);
    }
}
