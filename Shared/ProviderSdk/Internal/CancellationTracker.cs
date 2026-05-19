namespace ReportingPlatform.ProviderSdk.Internal;

internal sealed class CancellationTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _map = new();

    /// <summary>Track a new request. Returns a CT that cancels when Cancel(requestId) or CancelAll() is called, or when parentCt is cancelled.</summary>
    public CancellationToken Track(string requestId, CancellationToken parentCt)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        _map[requestId] = cts;
        return cts.Token;
    }

    public void Cancel(string requestId)
    {
        if (_map.TryGetValue(requestId, out var cts))
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void CancelAll()
    {
        foreach (var cts in _map.Values)
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Untrack(string requestId)
    {
        if (_map.TryRemove(requestId, out var cts))
            cts.Dispose();
    }

    public void Dispose()
    {
        foreach (var cts in _map.Values) cts.Dispose();
        _map.Clear();
    }
}
