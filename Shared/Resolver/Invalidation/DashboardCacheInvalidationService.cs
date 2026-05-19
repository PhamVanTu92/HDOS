namespace ReportingPlatform.Resolver.Invalidation;

/// <summary>
/// Background service that subscribes to Redis pub/sub for dashboard cache invalidation.
/// Channel pattern: <c>cache-invalidate:dashboard:*</c>
///
/// On receiving a message, L0 in-memory cache entries for the invalidated dashboard
/// will naturally expire (version-stamped keys are structurally unreachable after a
/// version bump). L1 Redis entries expire via TTL.
/// </summary>
internal sealed class DashboardCacheInvalidationService : IHostedService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DashboardCacheInvalidationService> _logger;

    public DashboardCacheInvalidationService(
        IConnectionMultiplexer redis,
        ILogger<DashboardCacheInvalidationService> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var sub = _redis.GetSubscriber();
        sub.Subscribe(
            RedisChannel.Pattern("cache-invalidate:dashboard:*"),
            (channel, message) =>
            {
                var code = ((string?)channel ?? string.Empty).Split(':').LastOrDefault() ?? string.Empty;
                _logger.LogInformation(
                    "Dashboard cache invalidation received: code={DashboardCode} msg={Message}",
                    code, (string?)message ?? "(empty)");

                // Widget cache keys are version-stamped — after the admin endpoint
                // bumps the version and publishes this event, subsequent renders will
                // fetch the new definition (with incremented version) and produce
                // cache keys that don't match any old entries.
                //
                // No explicit L0 eviction needed: keys are structurally unreachable
                // because they include the old version number.
                // L1 (Redis) entries expire via their configured TTL.
            });

        _logger.LogInformation("DashboardCacheInvalidationService subscribed to cache-invalidate:dashboard:*");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sub = _redis.GetSubscriber();
            sub.UnsubscribeAll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unsubscribing from dashboard cache invalidation channel");
        }
        return Task.CompletedTask;
    }
}
