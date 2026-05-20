namespace ReportingPlatform.IngestionApi.Models;

public sealed record IngestSingleRequest
{
    public required string EventType { get; init; }
    public required string OccurredAt { get; init; }
    public required JsonElement Payload { get; init; }
}
