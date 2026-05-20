using ReportingPlatform.Contracts.Enums;

namespace ReportingPlatform.Contracts.Messaging;

public sealed record OperationRequestMessage
{
    public required string RequestId { get; init; }
    public required string Operation { get; init; }

    // Serialized JSON string of the original params JsonElement.
    public required string ParamsJson { get; init; }

    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public string? CorrelationId { get; init; }
    public string? ConnectionId { get; init; }

    // Deadline derived from min(options.timeoutMs, registry.maxTimeoutMs) + DateTimeOffset.UtcNow.
    public required long TimeoutAtUnixMs { get; init; }

    public bool WantsProgress { get; init; }
    public Priority Priority { get; init; } = Priority.Normal;
    public int? CacheSeconds { get; init; }

    // W3C traceparent from Activity.Current at submission time.
    public required string Traceparent { get; init; }

    // Null for client-submitted requests. Set to the parent request's requestId
    // when the Resolver dispatches nested provider calls inside dashboard.render.
    public string? ParentRequestId { get; init; }

    // Optional provider routing hint. When set, Bridge prefers routing to the named
    // provider's queue. Falls back to round-robin if that provider has no active session.
    public string? ProviderId { get; init; }
}
