namespace ReportingPlatform.Router.Options;

public sealed class RouterOptions
{
    public const string Section = "Router";

    /// <summary>Messages pre-fetched from each priority queue. Keep equal to ConcurrentMessageLimit
    /// so the broker requeues promptly on worker restart.</summary>
    public int PrefetchCount { get; init; } = 4;

    /// <summary>Max concurrent in-flight Consume() calls across all priority queues combined.</summary>
    public int ConcurrentMessageLimit { get; init; } = 4;

    /// <summary>RabbitMQ x-message-ttl per queue (ms). Matches MaxTimeoutMs hard cap (Phase 5).
    /// Messages older than their own deadline are rejected as DEADLINE_EXCEEDED by the dispatcher.</summary>
    public int MessageTtlMs { get; init; } = 600_000;

    /// <summary>Seconds to drain in-flight messages after SIGTERM before force-stop.</summary>
    public int ShutdownTimeoutSeconds { get; init; } = 30;
}
