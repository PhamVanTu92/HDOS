namespace ReportingPlatform.Contracts.Messaging;

public sealed record CancelRequestMessage
{
    public required string RequestId { get; init; }
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
}
