namespace ReportingPlatform.Contracts.Envelopes;

public sealed record IngestEventEnvelope
{
    public required string EventType { get; init; }
    public required string TenantId { get; init; }
    public required string OccurredAt { get; init; }
    public required JsonElement Payload { get; init; }
}
