namespace ReportingPlatform.Operations.Context;

public sealed record OperationHandlerContext
{
    public required string      RequestId  { get; init; }
    public required string      TenantId   { get; init; }
    public required string      UserId     { get; init; }

    /// <summary>Pre-validated params from the request envelope.</summary>
    public required JsonElement Params     { get; init; }

    /// <summary>
    /// Non-null when <see cref="ReportingPlatform.Contracts.Envelopes.RequestOptions.Progress"/> was true.
    /// Handlers report progress via <c>Report((percent, message))</c>.
    /// Fire-and-forget — do not await.
    /// </summary>
    public IProgress<ProgressUpdate>? Progress { get; init; }

    /// <summary>W3C traceparent from originating request. Restore for child Activity spans.</summary>
    public required string Traceparent { get; init; }

    /// <summary>Unix epoch milliseconds deadline from the originating request. Used by nested adapters to clamp their own timeout.</summary>
    public long TimeoutAtUnixMs { get; init; }
}
