namespace ReportingPlatform.Caching;

// Central registry of Redis key patterns. All keys are prefixed with "rp:" (reporting platform).
public static class RedisKeys
{
    // Owner store: records which Gateway connection owns a requestId. TTL=10min.
    // Key: rp:owner:{requestId}
    public static string Owner(string requestId) => $"rp:owner:{requestId}";

    // Result store: serialized ResponseDispatchMessage. TTL=5min.
    // Key: rp:result:{requestId}
    public static string Result(string requestId) => $"rp:result:{requestId}";

    // Idempotency store: deduplication record. TTL matches request timeout.
    // Key: rp:idem:{tenantId}:{operationKey}
    public static string Idempotency(string tenantId, string operationKey) =>
        $"rp:idem:{tenantId}:{operationKey}";

    // Progress ring buffer (Redis Stream). TTL=30s.
    // Key: rp:progress:{requestId}
    public static string ProgressStream(string requestId) => $"rp:progress:{requestId}";

    // Single-flight lock: prevents duplicate concurrent fan-outs for the same cache key.
    // Key: rp:sflight:{cacheKey}
    public static string SingleFlightLock(string cacheKey) => $"rp:sflight:{cacheKey}";

    // Widget-level cached result (tenant-scoped). TTL=configurable per operation.
    // Key: rp:wcache:{tenantId}:{cacheKey}
    public static string WidgetCache(string tenantId, string cacheKey) =>
        $"rp:wcache:{tenantId}:{cacheKey}";
}
