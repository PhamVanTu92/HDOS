using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Responses;

namespace ReportingPlatform.Contracts.Messaging;

public sealed record OperationResponseMessage
{
    public required string RequestId { get; init; }
    public required ResponseStatus Status { get; init; }
    // Operation name, e.g. "dashboard.render". Nullable for backward compatibility with
    // any pre-Phase-7 code paths that create the record without setting it.
    public string? Operation { get; init; }
    public string? PayloadJson { get; init; }
    public ErrorDetail? Error { get; init; }
    public long ElapsedMs { get; init; }
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public string? ConnectionId { get; init; }
    public string? CorrelationId { get; init; }
}
