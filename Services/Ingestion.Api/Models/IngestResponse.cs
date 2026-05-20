namespace ReportingPlatform.IngestionApi.Models;

public sealed record IngestResponse
{
    public required int Accepted { get; init; }
    public required IReadOnlyList<string> EventIds { get; init; }
}

public sealed record IngestErrorResponse
{
    public required string Error { get; init; }
    public required string Message { get; init; }
}
