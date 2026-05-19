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

    // --- Phase 7: Gateway / SSE / orphan detection keys ---

    // Submission log: presence proves request was submitted. TTL = MessageTtlMs × 3 (30 min).
    // Used by OrphanDetector to return "orphaned" vs "not_found" on GET /result 404.
    // Key: rp:sublog:{requestId}
    public static string SubmissionLog(string requestId) => $"rp:sublog:{requestId}";

    // Active-progress Set: Set of requestIds currently expecting SSE progress events.
    // SADD on submit (options.progress=true); SREM after terminal dispatch.
    // Key: rp:active-progress  (single Set, not per-requestId)
    public const string ActiveProgress = "rp:active-progress";

    // SSE pub/sub channel: ProgressRelayWorker publishes here; Request.Api nodes fan out to SSE.
    // Channel: rp:sse-notify:{requestId}
    public static string SseNotify(string requestId) => $"rp:sse-notify:{requestId}";

    // SSE terminal pub/sub channel: ResponseRouter publishes here to close open SSE streams.
    // Channel: rp:sse-terminal:{requestId}
    public static string SseTerminal(string requestId) => $"rp:sse-terminal:{requestId}";
}
