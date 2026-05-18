using MessagePack;

namespace ReportingPlatform.Contracts.Envelopes;

[MessagePackObject]
public sealed record SubmitAck
{
    [Key("requestId")]
    public required string RequestId { get; init; }

    // ISO 8601 UTC string — Option B: backend sets this as a formatted string
    // before serialization; avoids DateTimeOffset formatter concerns for both
    // HTTP and MessagePack (SignalR Invoke return value).
    [Key("queuedAt")]
    public required string QueuedAt { get; init; }

    [Key("progressStreamUrl")]
    public string? ProgressStreamUrl { get; init; }
}
