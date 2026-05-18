using ReportingPlatform.Contracts.Enums;

namespace ReportingPlatform.Contracts.Store;

public sealed record IdempotencyRecord
{
    public required string RequestId { get; init; }
    // Hash of operation + params — the deduplication key.
    public required string OperationKey { get; init; }
    public required IdempotencyStatus Status { get; init; }
    // ISO 8601 UTC string. See DECISIONS.md §Coding standards / Timestamps.
    public required string CreatedAt { get; init; }
}
