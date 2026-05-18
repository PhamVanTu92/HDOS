namespace ReportingPlatform.Contracts.Store;

public sealed record ProgressEvent
{
    public required string RequestId { get; init; }
    public required int Percent { get; init; }
    public required string Message { get; init; }
    // ISO 8601 UTC string. See DECISIONS.md §Coding standards / Timestamps.
    public required string Timestamp { get; init; }
    public string? Step { get; init; }
    // Set when read back from the Redis Stream; null when first constructed.
    public string? EventId { get; init; }
}
