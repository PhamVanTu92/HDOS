namespace ReportingPlatform.ProviderBridge.Tests.Helpers;

public sealed class FakeProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<string, ProviderRegistration> _byId       = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderRegistration> _byClientId = new(StringComparer.Ordinal);

    // Injected BCrypt verifier — default uses real BCrypt.
    public Func<string, string, bool> CredentialVerifier { get; set; } =
        (secret, hash) => BCrypt.Net.BCrypt.Verify(secret, hash);

    // Count how many times credential verification was called.
    public int VerifyCallCount { get; private set; }

    public void Add(ProviderRegistration reg)
    {
        _byId[reg.ProviderId]   = reg;
        _byClientId[reg.ClientId] = reg;
    }

    public Task<ProviderRegistration?> GetAsync(string providerId, CancellationToken ct = default)
    {
        _byId.TryGetValue(providerId, out var reg);
        return Task.FromResult(reg is { Status: "active" } ? reg : null);
    }

    public Task<IReadOnlyList<ProviderRegistration>> GetAllActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ProviderRegistration> list = _byId.Values
            .Where(r => r.Status == "active").ToList();
        return Task.FromResult(list);
    }

    public Task<bool> ValidateCredentialsAsync(string clientId, string clientSecret, CancellationToken ct = default)
    {
        if (!_byClientId.TryGetValue(clientId, out var reg))
            return Task.FromResult(false);
        if (!string.Equals(reg.Status, "active", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);

        VerifyCallCount++;
        if (CredentialVerifier(clientSecret, reg.ClientSecretHash))
            return Task.FromResult(true);

        // Grace period: same logic as production PostgresProviderRegistry.
        if (reg.PendingClientSecretHash is not null
            && reg.PendingSecretExpiresAt > DateTimeOffset.UtcNow
            && CredentialVerifier(clientSecret, reg.PendingClientSecretHash))
            return Task.FromResult(true);

        return Task.FromResult(false);
    }

    public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;

    // Helper to build a provider with a known secret.
    public static ProviderRegistration BuildProvider(
        string providerId = "test-provider",
        string clientId   = "test-client",
        string secret     = "test-secret",
        string status     = "active",
        string? pendingHash = null,
        DateTimeOffset? pendingExpiry = null)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(secret, workFactor: 4); // low cost for tests
        return new ProviderRegistration
        {
            ProviderId              = providerId,
            DisplayName             = "Test Provider",
            ClientId                = clientId,
            ClientSecretHash        = hash,
            Operations              = ["test.op"],
            ChartTypes              = [],
            Transformers            = [],
            TimeoutMs               = 30_000,
            CircuitBreaker          = new CircuitBreakerConfig(),
            Status                  = status,
            MaxConcurrentRequests   = 4,
            PendingClientSecretHash = pendingHash,
            PendingSecretExpiresAt  = pendingExpiry,
        };
    }
}
