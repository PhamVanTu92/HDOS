namespace ReportingPlatform.ProviderSdk.Internal;

/// <summary>Internal delegate: given a raw OperationRequest + writer + SP + CT, execute the handler and write Terminal.</summary>
internal delegate Task HandlerDelegate(
    OperationRequest request,
    IAsyncStreamWriter<FromProvider> writer,
    SemaphoreSlim writeLock,
    IServiceProvider sp,
    CancellationToken ct);

internal sealed class HandlerRegistry
{
    private readonly Dictionary<string, HandlerDelegate> _handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a type-safe handler for TParams/TResult via a capturing delegate.</summary>
    public void Register<TParams, TResult>(string operation)
        where TParams : class
        where TResult : class
    {
        _handlers[operation] = async (request, writer, writeLock, sp, ct) =>
        {
            var handler  = sp.GetRequiredService<IOperationHandler<TParams, TResult>>();
            TParams @params;
            try
            {
                @params = JsonSerializer.Deserialize<TParams>(request.ParamsJson)
                    ?? throw new InvalidOperationException("Params deserialized to null.");
            }
            catch (Exception ex)
            {
                await WriteTerminalAsync(writer, writeLock, request.RequestId, ReportingPlatform.Provider.V1.Status.Failed,
                    null, new Error { Code = "VALIDATION_ERROR", Message = $"Failed to deserialize params: {ex.Message}" },
                    0, ct);
                return;
            }

            var sw       = Stopwatch.StartNew();
            var progress = new ProgressReporterImpl(request.RequestId, writer, request.WantsProgress, writeLock);
            var ctx      = new OperationContext<TParams>
            {
                RequestId     = request.RequestId,
                Operation     = request.Operation,
                Params        = @params,
                TenantId      = request.TenantId,
                UserId        = request.UserId,
                Deadline      = DateTimeOffset.FromUnixTimeMilliseconds(request.TimeoutAtUnixMs),
                WantsProgress = request.WantsProgress,
                Traceparent   = request.Traceparent,
                CorrelationId = request.CorrelationId,
                Progress      = new ProgressReporter(progress),
            };

            // Propagate W3C traceparent
            Activity? activity = null;
            if (!string.IsNullOrEmpty(request.Traceparent))
            {
                var actCtx = ActivityContext.TryParse(request.Traceparent, null, out var parsed) ? parsed : default;
                activity = ProviderSdkActivitySource.Source.StartActivity(
                    request.Operation,
                    ActivityKind.Server,
                    actCtx);
                activity?.SetTag("request.id", request.RequestId);
                activity?.SetTag("tenant.id", request.TenantId);
            }

            try
            {
                var result = await handler.HandleAsync(ctx, ct);
                sw.Stop();

                switch (result.SdkStatus)
                {
                    case SdkStatus.Done:
                        var payloadJson = JsonSerializer.Serialize(result.Payload);
                        await WriteTerminalAsync(writer, writeLock, request.RequestId, ReportingPlatform.Provider.V1.Status.Done,
                            payloadJson, null, sw.ElapsedMilliseconds, ct);
                        break;
                    case SdkStatus.Failed:
                        await WriteTerminalAsync(writer, writeLock, request.RequestId, ReportingPlatform.Provider.V1.Status.Failed,
                            null, new Error { Code = result.Err!.Code, Message = result.Err.Message, DetailsJson = result.Err.DetailsJson ?? "" },
                            sw.ElapsedMilliseconds, ct);
                        break;
                    case SdkStatus.Cancelled:
                        await WriteTerminalAsync(writer, writeLock, request.RequestId, ReportingPlatform.Provider.V1.Status.Cancelled,
                            null, null, sw.ElapsedMilliseconds, ct);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                await WriteTerminalAsync(writer, writeLock, request.RequestId, ReportingPlatform.Provider.V1.Status.Cancelled,
                    null, null, sw.ElapsedMilliseconds, CancellationToken.None);
            }
            catch (Exception ex)
            {
                sw.Stop();
                await WriteTerminalAsync(writer, writeLock, request.RequestId, ReportingPlatform.Provider.V1.Status.Failed,
                    null, new Error { Code = "INTERNAL_ERROR", Message = "Handler threw an unexpected exception." },
                    sw.ElapsedMilliseconds, CancellationToken.None);
                // Don't rethrow — dispatcher catches internally
                _ = ex; // suppress warning
            }
            finally
            {
                activity?.Dispose();
            }
        };
    }

    public HandlerDelegate? Resolve(string operation) =>
        _handlers.TryGetValue(operation, out var del) ? del : null;

    public IReadOnlyList<string> RegisteredOperations => _handlers.Keys.ToList();

    private static async Task WriteTerminalAsync(
        IAsyncStreamWriter<FromProvider> writer,
        SemaphoreSlim writeLock,
        string requestId,
        ReportingPlatform.Provider.V1.Status status,
        string? payloadJson,
        Error? error,
        long elapsedMs,
        CancellationToken ct)
    {
        var terminal = new Terminal { Status = status, ElapsedMs = elapsedMs };
        if (payloadJson is not null) terminal.PayloadJson = payloadJson;
        if (error is not null)       terminal.Error = error;

        await writeLock.WaitAsync(ct);
        try
        {
            await writer.WriteAsync(new FromProvider
            {
                ResponseChunk = new OperationResponseChunk { RequestId = requestId, Terminal = terminal }
            }, ct);
        }
        finally { writeLock.Release(); }
    }
}
