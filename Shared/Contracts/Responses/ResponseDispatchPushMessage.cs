using MessagePack;

namespace ReportingPlatform.Contracts.Responses;

// MessagePack model — pushed server → client via SignalR hub methods
// (RequestCompleted, RequestFailed, RequestCancelled).
// PayloadJson carries the raw JSON string; client calls JSON.parse() on it.
[MessagePackObject]
public sealed record ResponseDispatchPushMessage
{
    [Key("requestId")]
    public required string RequestId { get; init; }

    // Lowercase string matching ResponseStatus enum wire values: "done" | "failed" | "timeout" | "cancelled"
    [Key("status")]
    public required string Status { get; init; }

    [Key("operation")]
    public required string Operation { get; init; }

    [Key("payloadJson")]
    public string? PayloadJson { get; init; }

    [Key("error")]
    public ErrorDetail? Error { get; init; }

    [Key("elapsedMs")]
    public long ElapsedMs { get; init; }

    [Key("tenantId")]
    public required string TenantId { get; init; }
}
