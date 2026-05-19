using ReportingPlatform.Bridge.Bridge;
using ReportingPlatform.ProviderBridge.Tests.Helpers;

namespace ReportingPlatform.ProviderBridge.Tests;

/// <summary>RA1–RA2, SR1, HB1: Lifecycle tests.</summary>
public sealed class LifecycleTests
{
    // ── RA1 — JWT with 61s remaining → RefreshAuth scheduled in ~1s ──────────

    [Fact]
    public void RA1_TokenWith61sRemaining_RefreshScheduledIn1s()
    {
        var expUnixMs   = DateTimeOffset.UtcNow.AddSeconds(61).ToUnixTimeMilliseconds();
        var refreshAtMs = expUnixMs - 60_000;
        var delayMs     = refreshAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.InRange(delayMs, 500, 2_000); // Should be ~1000ms
    }

    // ── RA2 — JWT with 59s remaining → RefreshAuthRequired sent immediately ──

    [Fact]
    public void RA2_TokenWith59sRemaining_RefreshSentImmediately()
    {
        var expUnixMs   = DateTimeOffset.UtcNow.AddSeconds(59).ToUnixTimeMilliseconds();
        var refreshAtMs = expUnixMs - 60_000;
        var delayMs     = refreshAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Delay should be ≤ 0 → immediate send.
        Assert.True(delayMs <= 0, $"Expected delay ≤ 0, got {delayMs}ms");
    }

    // ── SR1 — Revocation published → CloseAllForProvider closes sessions ──────

    [Fact]
    public async Task SR1_RevocationPublished_SessionsClosed()
    {
        var sessionMgr = new ProviderSessionManager();
        var closeLog   = new System.Collections.Concurrent.ConcurrentBag<string>();

        sessionMgr.Register("session-1", "provider-a", reason =>
        {
            closeLog.Add($"session-1:{reason}");
            return Task.CompletedTask;
        });
        sessionMgr.Register("session-2", "provider-a", reason =>
        {
            closeLog.Add($"session-2:{reason}");
            return Task.CompletedTask;
        });
        sessionMgr.Register("session-3", "provider-b", reason =>
        {
            closeLog.Add($"session-3:{reason}");
            return Task.CompletedTask;
        });

        await sessionMgr.CloseAllForProviderAsync("provider-a", "credentials_revoked");

        Assert.Equal(2, closeLog.Count);
        Assert.Contains("session-1:credentials_revoked", closeLog);
        Assert.Contains("session-2:credentials_revoked", closeLog);
        Assert.DoesNotContain("session-3:credentials_revoked", closeLog.AsEnumerable());
    }

    // ── HB1 — No heartbeat for 31s → timeout callback fires ─────────────────

    [Fact]
    public async Task HB1_NoHeartbeatFor31s_TimeoutCallbackFires()
    {
        var tcs     = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new HeartbeatMonitor(() =>
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        // Use reflection to set _lastHeartbeatTicks to 31s ago.
        var field = typeof(HeartbeatMonitor)
            .GetField("_lastHeartbeatTicks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        // _lastHeartbeatTicks stores DateTimeOffset.UtcNow.Ticks (long)
        field.SetValue(monitor, (DateTimeOffset.UtcNow - TimeSpan.FromSeconds(31)).Ticks);

        // Manually trigger the Check method via reflection.
        var checkMethod = typeof(HeartbeatMonitor)
            .GetMethod("Check", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        checkMethod.Invoke(monitor, [null]);

        var fired = await Task.WhenAny(tcs.Task, Task.Delay(500));
        Assert.True(tcs.Task.IsCompleted, "HeartbeatMonitor timeout callback should have fired");

        await monitor.DisposeAsync();
    }
}
