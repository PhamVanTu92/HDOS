using MessagePack;

namespace ReportingPlatform.HubContracts;

/// <summary>
/// Terminal response pushed server → client via SignalR.
/// MessagePack-annotated so the binary SignalR protocol serialises it efficiently.
/// </summary>
[MessagePackObject]
public sealed record ResponseDispatchPushMessage
{
    [Key("requestId")]
    public required string RequestId { get; init; }

    /// <summary><c>done</c> | <c>failed</c> | <c>timeout</c> | <c>cancelled</c></summary>
    [Key("status")]
    public required string Status { get; init; }

    /// <summary>Dot-notation operation name, e.g. <c>dashboard.render</c>.</summary>
    [Key("operation")]
    public string? Operation { get; init; }

    /// <summary>Raw JSON string of the result payload. Non-null on success.</summary>
    [Key("payload")]
    public string? PayloadJson { get; init; }

    [Key("error")]
    public ErrorDetail? Error { get; init; }

    [Key("elapsedMs")]
    public long ElapsedMs { get; init; }

    [Key("tenantId")]
    public required string TenantId { get; init; }
}
