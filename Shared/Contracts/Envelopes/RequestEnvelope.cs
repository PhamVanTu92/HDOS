namespace ReportingPlatform.Contracts.Envelopes;

public sealed record RequestEnvelope
{
    public required string RequestId { get; init; }
    public required string Operation { get; init; }

    // Arbitrary operation parameters. Validated against the registered JSON Schema
    // for the operation after structural envelope validation passes.
    public required JsonElement Params { get; init; }

    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public string? CorrelationId { get; init; }
    public RequestOptions Options { get; init; } = new();

    // Optional provider routing hint. When set, Bridge prefers routing to the named
    // provider's queue. Falls back to round-robin if that provider has no active session.
    public string? ProviderId { get; init; }
}
