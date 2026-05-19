using Polly.CircuitBreaker;
using ReportingPlatform.Bridge.Resilience;

namespace ReportingPlatform.ProviderBridge.Tests;

/// <summary>CB1–CB2, PC1–PC2: Resilience tests.</summary>
public sealed class ResilienceTests
{
    private static ProviderRegistration BuildReg(int failureThresholdPct = 50) =>
        new()
        {
            ProviderId            = "resilience-provider",
            DisplayName           = "Resilience Provider",
            ClientId              = "res-client",
            ClientSecretHash      = "hash",
            Operations            = ["test.op"],
            ChartTypes            = [],
            Transformers          = [],
            TimeoutMs             = 2_000,
            CircuitBreaker        = new CircuitBreakerConfig
            {
                FailureThreshold = failureThresholdPct,
                WindowSeconds    = 10,
                CooldownSeconds  = 1,
            },
            Status                = "active",
            MaxConcurrentRequests = 4,
        };

    // ── CB1 — Enough failures → circuit opens → BrokenCircuitException ───────

    [Fact]
    public async Task CB1_CircuitBreaker_OpensAfterThreshold()
    {
        var factory  = new ProviderResiliencePipeline();
        var reg      = BuildReg(failureThresholdPct: 50);
        var pipeline = factory.GetOrCreate(reg);

        // Fire 3+ failures to meet minimum throughput (3) and exceed 50% threshold.
        for (int i = 0; i < 4; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    await Task.Delay(1, ct);
                    throw new InvalidOperationException("provider failed");
                });
            }
            catch (InvalidOperationException) { /* expected */ }
            catch (BrokenCircuitException)    { /* circuit opened */ }
        }

        // Now circuit should be open — next call throws BrokenCircuitException.
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(async () =>
            await pipeline.ExecuteAsync(ct =>
            {
                throw new InvalidOperationException("should not reach provider");
#pragma warning disable CS0162 // Unreachable code detected
                return ValueTask.CompletedTask;
#pragma warning restore CS0162
            }));
    }

    // ── CB2 — Circuit opens → wait cooldown → half-open → next call passes ──

    [Fact]
    public async Task CB2_CircuitBreaker_ClosesAfterCooldown()
    {
        var factory  = new ProviderResiliencePipeline();
        var reg      = BuildReg(failureThresholdPct: 50);
        var pipeline = factory.GetOrCreate(reg);

        // Open the circuit.
        for (int i = 0; i < 4; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    await Task.Delay(1, ct);
                    throw new InvalidOperationException("provider failed");
                });
            }
            catch { /* expected */ }
        }

        // Wait for cooldown (1s configured).
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Circuit should be half-open — successful call closes it.
        var result = await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(1, ct);
            return 42;
        });
        Assert.Equal(42, result);
    }

    // ── PC1 — ProviderSessionManager: session removed on Unregister ──────────

    [Fact]
    public async Task PC1_SessionManager_SessionRemovedOnUnregister()
    {
        var mgr = new ProviderSessionManager();
        Assert.Equal(0, mgr.TotalSessions);

        mgr.Register("s1", "p1", _ => Task.CompletedTask);
        Assert.Equal(1, mgr.TotalSessions);

        mgr.Unregister("s1");
        Assert.Equal(0, mgr.TotalSessions);
        await Task.CompletedTask; // async test
    }

    // ── PC2 — CloseAllForProvider only closes matching sessions ──────────────

    [Fact]
    public async Task PC2_CloseAllForProvider_OnlyClosesMatchingSessions()
    {
        var mgr     = new ProviderSessionManager();
        var closed  = new System.Collections.Concurrent.ConcurrentBag<string>();

        mgr.Register("s1", "provider-x", r => { closed.Add("s1"); return Task.CompletedTask; });
        mgr.Register("s2", "provider-y", r => { closed.Add("s2"); return Task.CompletedTask; });
        mgr.Register("s3", "provider-x", r => { closed.Add("s3"); return Task.CompletedTask; });

        await mgr.CloseAllForProviderAsync("provider-x", "test");

        Assert.Contains("s1", closed);
        Assert.Contains("s3", closed);
        Assert.DoesNotContain("s2", closed.AsEnumerable());
    }
}
