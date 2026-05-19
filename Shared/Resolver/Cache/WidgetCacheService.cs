namespace ReportingPlatform.Resolver.Cache;

/// <summary>
/// Two-level widget result cache.
///
/// L0 — <see cref="IMemoryCache"/>: in-process, zero-serialization cost.
/// L1 — Redis <see cref="IDatabase"/>: cross-process, JSON-serialized <see cref="WidgetEnvelope"/>.
///
/// Cache key format: <c>widget:{tenantId}:{dashCode}:v{version}:{widgetId}:{filtersHash}</c>
///
/// Version-stamped keys mean old cache entries are structurally unreachable after a version
/// bump — they expire via TTL without requiring active eviction.
/// </summary>
public sealed class WidgetCacheService
{
    private readonly IMemoryCache _l0;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<WidgetCacheService> _logger;

    private static readonly JsonSerializerOptions SerOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Full constructor for production use (L0 + L1 Redis).</summary>
    public WidgetCacheService(
        IMemoryCache l0,
        IConnectionMultiplexer redis,
        ILogger<WidgetCacheService> logger)
    {
        _l0     = l0;
        _redis  = redis;
        _logger = logger;
    }

    /// <summary>
    /// L0-only constructor for tests — Redis is disabled; all L1 operations are no-ops.
    /// </summary>
    internal WidgetCacheService(
        IMemoryCache l0,
        ILogger<WidgetCacheService> logger)
    {
        _l0     = l0;
        _redis  = null;
        _logger = logger;
    }

    public static string MakeKey(
        string tenantId,
        string dashCode,
        string version,
        string widgetId,
        string filtersHash)
        => $"widget:{tenantId}:{dashCode}:v{version}:{widgetId}:{filtersHash}";

    /// <summary>
    /// Returns the cached envelope or null on a miss.
    /// Promotes L1 hits to L0 to warm future requests.
    /// </summary>
    public async Task<WidgetEnvelope?> GetAsync(string key, CancellationToken ct = default)
    {
        // L0 check
        if (_l0.TryGetValue(key, out WidgetEnvelope? cached))
        {
            _logger.LogDebug("Cache L0 hit: {Key}", key);
            return cached;
        }

        // L1 check
        try
        {
            if (_redis is null) return null;
            var db  = _redis.GetDatabase();
            var raw = await db.StringGetAsync(key);
            if (!raw.HasValue)
                return null;

            var envelope = JsonSerializer.Deserialize<WidgetEnvelope>(raw.ToString(), SerOpts);
            if (envelope is null)
                return null;

            _logger.LogDebug("Cache L1 hit: {Key}", key);

            // Promote to L0 with short TTL (avoid thundering herd on Redis miss)
            _l0.Set(key, envelope, TimeSpan.FromSeconds(30));
            return envelope;
        }
        catch (Exception ex)
        {
            // Redis failures are non-fatal — continue with cache miss
            _logger.LogWarning(ex, "Redis get failed for key {Key}", key);
            return null;
        }
    }

    /// <summary>Stores the envelope in both cache levels.</summary>
    public async Task SetAsync(string key, WidgetEnvelope envelope, int ttlSeconds)
    {
        var ttl = TimeSpan.FromSeconds(ttlSeconds);

        // L0
        _l0.Set(key, envelope, ttl);

        // L1
        try
        {
            if (_redis is null) return;
            var db   = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(envelope, SerOpts);
            await db.StringSetAsync(key, json, ttl);
        }
        catch (Exception ex)
        {
            // Redis failures are non-fatal — L0 is still populated
            _logger.LogWarning(ex, "Redis set failed for key {Key}", key);
        }
    }

    /// <summary>
    /// Evicts an entry from L0 only (Redis entries expire via TTL).
    /// Called by <see cref="Invalidation.DashboardCacheInvalidationService"/> on version bump.
    /// </summary>
    public void EvictFromL0(string keyPrefix)
    {
        // IMemoryCache doesn't support prefix-eviction; we rely on TTL expiry for L0.
        // Callers should pass the full key when known. Prefix eviction is not supported.
        _l0.Remove(keyPrefix);
    }
}
