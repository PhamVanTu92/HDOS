namespace ReportingPlatform.IngestionApi.Models;

public sealed record IngestBatchRequest
{
    public required IReadOnlyList<IngestSingleRequest> Events { get; init; }
}
