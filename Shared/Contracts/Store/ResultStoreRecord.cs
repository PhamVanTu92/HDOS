using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Responses;

namespace ReportingPlatform.Contracts.Store;

public sealed record ResultStoreRecord
{
    public required string RequestId { get; init; }
    public required ResponseStatus Status { get; init; }
    public string? PayloadJson { get; init; }
    public ErrorDetail? Error { get; init; }
    public long ElapsedMs { get; init; }
    public required string TenantId { get; init; }
    public required DateTimeOffset StoredAt { get; init; }
}
