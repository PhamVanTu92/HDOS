namespace ReportingPlatform.Contracts.Messaging;

public sealed record OperationProgressMessage
{
    public required string RequestId { get; init; }
    public required int Percent { get; init; }
    public required string Message { get; init; }
    public required long TsUnixMs { get; init; }
    public required string TenantId { get; init; }
}
