namespace ReportingPlatform.Contracts.Responses;

// SSE transport only — never pushed via SignalR, no MessagePack needed.
public sealed record ResponseProgressMessage
{
    public required string RequestId { get; init; }
    public required int Percent { get; init; }
    public required string Message { get; init; }
    public required long TsUnixMs { get; init; }
}
