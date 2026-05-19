namespace ReportingPlatform.ProviderSdk.Internal;

internal sealed class RequestDispatcher
{
    private readonly HandlerRegistry _registry;
    private readonly IServiceProvider _sp;
    private readonly IAsyncStreamWriter<FromProvider> _writer;
    private readonly SemaphoreSlim _writeLock;
    private readonly SemaphoreSlim _concurrency;
    private readonly CancellationTracker _cancellation = new();
    private readonly ILogger _logger;
    private volatile bool _holdNew;
    private int _activeCount;

    public bool HoldNew { get => _holdNew; set => _holdNew = value; }
    public int ActiveCount => _activeCount;

    public RequestDispatcher(
        HandlerRegistry registry,
        IServiceProvider sp,
        IAsyncStreamWriter<FromProvider> writer,
        SemaphoreSlim writeLock,
        int maxConcurrent,
        ILogger logger)
    {
        _registry    = registry;
        _sp          = sp;
        _writer      = writer;
        _writeLock   = writeLock;
        _concurrency = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        _logger      = logger;
    }

    public void DispatchFireAndForget(OperationRequest request, CancellationToken streamCt)
    {
        _ = Task.Run(() => DispatchAsync(request, streamCt));
    }

    private async Task DispatchAsync(OperationRequest request, CancellationToken streamCt)
    {
        if (_holdNew)
        {
            // Draining — return CANCELLED immediately
            await WriteQuickCancelAsync(request.RequestId, CancellationToken.None);
            return;
        }

        var handler = _registry.Resolve(request.Operation);
        if (handler is null)
        {
            _logger.LogWarning("No handler for operation {Operation}", request.Operation);
            await WriteQuickCancelAsync(request.RequestId, CancellationToken.None);
            return;
        }

        await _concurrency.WaitAsync(streamCt);
        Interlocked.Increment(ref _activeCount);
        var requestCt = _cancellation.Track(request.RequestId, streamCt);

        try
        {
            using var scope = (_sp as IServiceScopeFactory) is not null
                ? _sp.CreateScope()
                : null;
            var scopedSp = scope?.ServiceProvider ?? _sp;
            await handler(request, _writer, _writeLock, scopedSp, requestCt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled exception dispatching {RequestId}", request.RequestId);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
            _concurrency.Release();
            _cancellation.Untrack(request.RequestId);
        }
    }

    public void Cancel(string requestId) => _cancellation.Cancel(requestId);

    public void CancelAll() => _cancellation.CancelAll();

    public async Task WaitForDrainAsync(CancellationToken ct)
    {
        while (_activeCount > 0 && !ct.IsCancellationRequested)
            await Task.Delay(50, ct);
    }

    private async Task WriteQuickCancelAsync(string requestId, CancellationToken ct)
    {
        try
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                await _writer.WriteAsync(new FromProvider
                {
                    ResponseChunk = new OperationResponseChunk
                    {
                        RequestId = requestId,
                        Terminal  = new Terminal { Status = ReportingPlatform.Provider.V1.Status.Cancelled }
                    }
                }, ct);
            }
            finally { _writeLock.Release(); }
        }
        catch { /* best-effort */ }
    }
}
