namespace ReportingPlatform.ProviderSdk.Internal;

internal sealed class ProgressReporterImpl
{
    private readonly string _requestId;
    private readonly IAsyncStreamWriter<FromProvider> _writer;
    private readonly bool _wantsProgress;
    private readonly SemaphoreSlim _writeLock; // shared with HeartbeatSender

    public ProgressReporterImpl(string requestId, IAsyncStreamWriter<FromProvider> writer, bool wantsProgress, SemaphoreSlim writeLock)
    {
        _requestId     = requestId;
        _writer        = writer;
        _wantsProgress = wantsProgress;
        _writeLock     = writeLock;
    }

    public async Task ReportAsync(int percent, string message, CancellationToken ct = default)
    {
        if (!_wantsProgress) return;
        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteAsync(new FromProvider
            {
                ResponseChunk = new OperationResponseChunk
                {
                    RequestId = _requestId,
                    Progress = new Progress
                    {
                        Percent = Math.Clamp(percent, 1, 99),
                        Message = message,
                        TsUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    }
                }
            }, ct);
        }
        finally { _writeLock.Release(); }
    }
}
