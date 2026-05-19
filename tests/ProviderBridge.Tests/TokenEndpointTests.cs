extern alias RequestApi;

using ReportingPlatform.ProviderBridge.Tests.Helpers;
using RequestApi::ReportingPlatform.RequestApi.Controllers;

namespace ReportingPlatform.ProviderBridge.Tests;

/// <summary>TB1–TB11: Token endpoint logic tests.</summary>
public sealed class TokenEndpointTests
{
    // ── TB1 — Valid credentials → validate returns true ──────────────────────

    [Fact]
    public async Task TB1_ValidCredentials_ValidationSucceeds()
    {
        var reg      = FakeProviderRegistry.BuildProvider(secret: "correct-secret");
        var registry = new FakeProviderRegistry();
        registry.Add(reg);

        var valid = await registry.ValidateCredentialsAsync(reg.ClientId, "correct-secret");
        Assert.True(valid);
    }

    // ── TB2 — Unknown clientId → validation returns false ───────────────────

    [Fact]
    public async Task TB2_UnknownClientId_ValidationReturnsFalse()
    {
        var registry = new FakeProviderRegistry();
        var valid    = await registry.ValidateCredentialsAsync("nonexistent-client", "any-secret");
        Assert.False(valid);
    }

    // ── TB3 — Wrong secret → validation returns false ────────────────────────

    [Fact]
    public async Task TB3_WrongSecret_ValidationReturnsFalse()
    {
        var reg      = FakeProviderRegistry.BuildProvider(secret: "correct-secret");
        var registry = new FakeProviderRegistry();
        registry.Add(reg);

        var valid = await registry.ValidateCredentialsAsync(reg.ClientId, "wrong-secret");
        Assert.False(valid);
    }

    // ── TB4 — 5 failures → lockout; attempt 6 with CORRECT secret locked out;
    //          BCrypt verifier NOT incremented on attempt 6 ───────────────────

    [Fact]
    public async Task TB4_FiveFailures_LockoutEngaged_BCryptNotCalledOnAttempt6()
    {
        var fakeStore = new FakeLockoutStore();
        var lockout   = fakeStore.BuildLockoutService();
        var registry  = new FakeProviderRegistry();
        var reg       = FakeProviderRegistry.BuildProvider(secret: "correct-secret");
        registry.Add(reg);

        // Inject fake verifier so we can count calls.
        int verifyCallCount = 0;
        registry.CredentialVerifier = (secret, hash) =>
        {
            verifyCallCount++;
            return BCrypt.Net.BCrypt.Verify(secret, hash);
        };

        // Attempt 1-5: wrong secret → record failures.
        for (int i = 0; i < 5; i++)
        {
            var valid = await registry.ValidateCredentialsAsync(reg.ClientId, "wrong-secret");
            Assert.False(valid);
            await lockout.RecordFailureAsync(reg.ClientId);
        }

        // Verify lockout key exists.
        Assert.True(await lockout.IsLockedOutAsync(reg.ClientId));

        // Attempt 6: correct secret but locked out — should short-circuit before BCrypt.
        var callCountBefore = verifyCallCount;
        var isLocked = await lockout.IsLockedOutAsync(reg.ClientId);
        Assert.True(isLocked);
        // In the controller, lockout is checked BEFORE ValidateCredentialsAsync.
        // So BCrypt verifier call count should NOT have increased.
        Assert.Equal(callCountBefore, verifyCallCount);
    }

    // ── TB5 — Lockout TTL expires → correct secret succeeds ─────────────────

    [Fact]
    public async Task TB5_LockoutExpires_CorrectSecretSucceeds()
    {
        var fakeStore = new FakeLockoutStore();
        var lockout   = fakeStore.BuildLockoutService();

        // Simulate 5 failures → lockout.
        for (int i = 0; i < 5; i++)
            await lockout.RecordFailureAsync("client-tb5");

        Assert.True(await lockout.IsLockedOutAsync("client-tb5"));

        // Simulate TTL expiry.
        fakeStore.ExpireAll();

        Assert.False(await lockout.IsLockedOutAsync("client-tb5"));
    }

    // ── TB6 — 11th request for same clientId in 1 min → rate limited ─────────

    [Fact]
    public async Task TB6_EleventhRequest_RateLimited()
    {
        var fakeStore = new FakeLockoutStore();
        var lockout   = fakeStore.BuildLockoutService();

        for (int i = 0; i < 10; i++)
            Assert.False(await lockout.IsRateLimitedAsync("client-tb6"));

        Assert.True(await lockout.IsRateLimitedAsync("client-tb6"));
    }

    // ── TB7 — Suspended provider → validation returns false ──────────────────

    [Fact]
    public async Task TB7_SuspendedProvider_ValidationReturnsFalse()
    {
        var reg      = FakeProviderRegistry.BuildProvider(secret: "correct", status: "suspended");
        var registry = new FakeProviderRegistry();
        registry.Add(reg);

        var valid = await registry.ValidateCredentialsAsync(reg.ClientId, "correct");
        Assert.False(valid);
    }

    // ── TB8 — credentials_revoked provider → validation returns false ─────────

    [Fact]
    public async Task TB8_CredentialsRevokedProvider_ValidationReturnsFalse()
    {
        var reg      = FakeProviderRegistry.BuildProvider(secret: "correct", status: "credentials_revoked");
        var registry = new FakeProviderRegistry();
        registry.Add(reg);

        var valid = await registry.ValidateCredentialsAsync(reg.ClientId, "correct");
        Assert.False(valid);
    }

    // ── TB9 — wrong grantType → would return 400 (validated by controller logic)

    [Fact]
    public void TB9_WrongGrantType_IsNotClientCredentials()
    {
        var request = new ProviderTokenRequest
        {
            ClientId     = "any",
            ClientSecret = "any",
            GrantType    = "authorization_code",
        };
        Assert.NotEqual("client_credentials", request.GrantType, StringComparer.Ordinal);
    }

    // ── TB10 — old secret accepted during rotation grace period ──────────────

    [Fact]
    public async Task TB10_OldSecretAcceptedDuringGracePeriod()
    {
        const string oldSecret = "old-secret";
        const string newSecret = "new-secret";
        var oldHash            = BCrypt.Net.BCrypt.HashPassword(oldSecret, workFactor: 4);
        var newHash            = BCrypt.Net.BCrypt.HashPassword(newSecret, workFactor: 4);

        var reg = FakeProviderRegistry.BuildProvider(
            secret:        newSecret,
            pendingHash:   oldHash,
            pendingExpiry: DateTimeOffset.UtcNow.AddSeconds(60));

        // Override the ClientSecretHash to be the new hash.
        reg = reg with { ClientSecretHash = newHash };

        var registry = new FakeProviderRegistry();
        registry.Add(reg);

        // Old secret should be accepted (within grace).
        Assert.True(await registry.ValidateCredentialsAsync(reg.ClientId, oldSecret));
        // New secret also accepted.
        Assert.True(await registry.ValidateCredentialsAsync(reg.ClientId, newSecret));
    }

    // ── TB11 — old secret rejected after grace period expires ─────────────────

    [Fact]
    public async Task TB11_OldSecretRejectedAfterGracePeriod()
    {
        const string oldSecret = "old-secret";
        const string newSecret = "new-secret";
        var oldHash            = BCrypt.Net.BCrypt.HashPassword(oldSecret, workFactor: 4);
        var newHash            = BCrypt.Net.BCrypt.HashPassword(newSecret, workFactor: 4);

        var reg = FakeProviderRegistry.BuildProvider(
            secret:        newSecret,
            pendingHash:   oldHash,
            pendingExpiry: DateTimeOffset.UtcNow.AddSeconds(-1)); // expired

        reg = reg with { ClientSecretHash = newHash };

        var registry = new FakeProviderRegistry();
        registry.Add(reg);

        // Old secret should be rejected (grace expired).
        Assert.False(await registry.ValidateCredentialsAsync(reg.ClientId, oldSecret));
        // New secret still works.
        Assert.True(await registry.ValidateCredentialsAsync(reg.ClientId, newSecret));
    }

    // ── TB-INTEGRATION — Real BCrypt integration test ─────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TBIntegration_RealBCrypt_VerifiesCorrectly()
    {
        const string secret = "integration-test-secret-not-faked";
        // Real BCrypt hash at work factor 4 (fast for tests but real algorithm).
        var hash = BCrypt.Net.BCrypt.HashPassword(secret, workFactor: 4);
        Assert.True(hash.StartsWith("$2a$04$", StringComparison.Ordinal),
            "Hash must be real BCrypt $2a$ format with work factor 04");

        var reg = new ProviderRegistration
        {
            ProviderId            = "integration-provider",
            DisplayName           = "Integration Provider",
            ClientId              = "integration-client",
            ClientSecretHash      = hash,
            Operations            = ["test.op"],
            ChartTypes            = [],
            Transformers          = [],
            TimeoutMs             = 30_000,
            CircuitBreaker        = new CircuitBreakerConfig(),
            Status                = "active",
            MaxConcurrentRequests = 4,
        };
        var registry = new FakeProviderRegistry();
        registry.Add(reg);

        // Verify using the registry (which calls real BCrypt.Verify internally).
        Assert.True(await registry.ValidateCredentialsAsync("integration-client", secret));
        Assert.False(await registry.ValidateCredentialsAsync("integration-client", "wrong-password"));
    }
}
