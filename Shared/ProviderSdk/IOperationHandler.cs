namespace ReportingPlatform.ProviderSdk;

/// <summary>Implement to handle one registered operation type. Register via .Handle&lt;TParams, TResult&gt;(operation) on IProviderSdkBuilder.</summary>
public interface IOperationHandler<TParams, TResult>
    where TParams : class
    where TResult : class
{
    Task<OperationResult<TResult>> HandleAsync(OperationContext<TParams> context, CancellationToken ct);
}

/// <summary>Per-request context passed to each handler invocation.</summary>
public sealed class OperationContext<TParams> where TParams : class
{
    public required string         RequestId     { get; init; }
    public required string         Operation     { get; init; }
    public required TParams        Params        { get; init; }
    public required string         TenantId      { get; init; }
    public required string         UserId        { get; init; }
    public required DateTimeOffset Deadline      { get; init; }
    public required bool           WantsProgress { get; init; }
    public required string         Traceparent   { get; init; }
    public required string         CorrelationId { get; init; }
    /// <summary>Call ReportAsync(percent, message) to emit progress events. No-op if WantsProgress=false.</summary>
    public required ProgressReporter Progress    { get; init; }
}

/// <summary>Emits progress chunks over the active gRPC stream. Thread-safe.</summary>
public sealed class ProgressReporter
{
    private readonly Internal.ProgressReporterImpl _impl;
    internal ProgressReporter(Internal.ProgressReporterImpl impl) => _impl = impl;
    public Task ReportAsync(int percent, string message, CancellationToken ct = default) =>
        _impl.ReportAsync(percent, message, ct);
}

/// <summary>Return value from IOperationHandler.HandleAsync.</summary>
public sealed class OperationResult<TResult> where TResult : class
{
    internal SdkStatus SdkStatus  { get; private init; }
    internal TResult?  Payload    { get; private init; }
    internal OperationError? Err  { get; private init; }

    public static OperationResult<TResult> Success(TResult payload) =>
        new() { SdkStatus = SdkStatus.Done, Payload = payload };

    public static OperationResult<TResult> Failure(string code, string message, string? detailsJson = null) =>
        new() { SdkStatus = SdkStatus.Failed, Err = new OperationError(code, message, detailsJson) };

    public static OperationResult<TResult> Cancelled() =>
        new() { SdkStatus = SdkStatus.Cancelled };
}

internal enum SdkStatus { Done, Failed, Cancelled }

public sealed record OperationError(string Code, string Message, string? DetailsJson = null);
