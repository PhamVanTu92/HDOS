namespace ReportingPlatform.Bridge.Services;

public sealed class ProviderSessionManager
{
    private readonly ConcurrentDictionary<string, ProviderSessionHandle> _sessions = new();

    public void Register(string sessionId, string providerId, Func<string, Task> closeCallback)
    {
        _sessions[sessionId] = new ProviderSessionHandle(providerId, closeCallback);
    }

    public void Unregister(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    public async Task CloseAllForProviderAsync(string providerId, string reason)
    {
        var tasks = new List<Task>();
        foreach (var (_, handle) in _sessions)
        {
            if (string.Equals(handle.ProviderId, providerId, StringComparison.Ordinal))
                tasks.Add(handle.CloseCallback(reason));
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Returns true if any active session belongs to <paramref name="providerId"/>.
    /// Used by routing hint logic to decide whether to prefer a specific provider queue.
    /// Falls back to round-robin if this returns false (no active session for that provider).
    /// </summary>
    public bool HasActiveSession(string providerId)
    {
        foreach (var (_, handle) in _sessions)
        {
            if (string.Equals(handle.ProviderId, providerId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public int TotalSessions => _sessions.Count;
}

internal sealed record ProviderSessionHandle(string ProviderId, Func<string, Task> CloseCallback);
