namespace ReportingPlatform.Contracts.Store;

public sealed record OwnerStoreRecord
{
    public required string RequestId { get; init; }
    public string? ConnectionId { get; init; }
    public required string UserId { get; init; }
    public required string TenantId { get; init; }
    // ISO 8601 UTC string, e.g. "2026-05-18T10:00:00.000Z". See DECISIONS.md §Coding standards.
    public required string SubmittedAt { get; init; }
}
