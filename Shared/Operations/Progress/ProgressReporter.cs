using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.Operations.Progress;

/// <summary>
/// Bridges <see cref="IProgress{ProgressUpdate}"/> (synchronous) to
/// <see cref="IProgressBuffer.AppendAsync"/> (async) via a bounded channel.
/// Dropped events (channel full) are non-fatal — the client sees a gap in progress percentage.
/// </summary>
internal sealed class ProgressReporter : IProgress<ProgressUpdate>, IAsyncDisposable
{
    private readonly Channel<ProgressUpdate> _channel;
    private readonly Task _drainTask;
    private readonly IProgressBuffer _buffer;
    private readonly string _requestId;

    public ProgressReporter(IProgressBuffer buffer, string requestId)
    {
        _buffer    = buffer;
        _requestId = requestId;

        _channel = Channel.CreateBounded<ProgressUpdate>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        _drainTask = DrainAsync();
    }

    public void Report(ProgressUpdate update) =>
        _channel.Writer.TryWrite(update);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        await _drainTask;
    }

    private async Task DrainAsync()
    {
        await foreach (var update in _channel.Reader.ReadAllAsync())
        {
            var evt = new ProgressEvent
            {
                RequestId = _requestId,
                Percent   = update.Percent,
                Message   = update.Message,
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            };
            await _buffer.AppendAsync(evt);
        }
    }
}
