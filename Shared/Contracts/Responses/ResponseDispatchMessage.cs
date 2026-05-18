using ReportingPlatform.Contracts.Enums;

namespace ReportingPlatform.Contracts.Responses;

// STJ model — used for HTTP GET /result response and as the internal deserialized form.
// For SignalR push use ResponseDispatchPushMessage instead.
public sealed record ResponseDispatchMessage
{
    public required string RequestId { get; init; }
    public required ResponseStatus Status { get; init; }
    public required string Operation { get; init; }
    public JsonElement? Payload { get; init; }
    public ErrorDetail? Error { get; init; }
    public long ElapsedMs { get; init; }
    public required string TenantId { get; init; }
}
