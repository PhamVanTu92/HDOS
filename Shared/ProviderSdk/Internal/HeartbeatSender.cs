namespace ReportingPlatform.ProviderSdk.Internal;

internal sealed class HeartbeatSender : IAsyncDisposable
{
    private readonly IAsyncStreamWriter<FromProvider> _writer;
    private readonly SemaphoreSlim _writeLock;
    private readonly int _intervalSeconds;
    private readonly CancellationToken _ct;
    private Task? _task;

    public HeartbeatSender(IAsyncStreamWriter<FromProvider> writer, SemaphoreSlim writeLock, int intervalSeconds, CancellationToken ct)
    {
        _writer          = writer;
        _writeLock       = writeLock;
        _intervalSeconds = Math.Max(1, intervalSeconds);
        _ct              = ct;
    }

    public void Start() => _task = RunAsync();

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        while (!_ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(_ct)) break;
                await _writeLock.WaitAsync(_ct);
                try
                {
                    await _writer.WriteAsync(new FromProvider
                    {
                        Heartbeat = new Heartbeat { TsUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                    }, _ct);
                }
                finally { _writeLock.Release(); }
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_task is not null) await _task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
