namespace ReportingPlatform.ProviderSdk;

public sealed class ProviderSdkOptions
{
    /// <summary>MUST match jwt.sub. Corresponds to provider_registry.provider_id.</summary>
    public required string ProviderId { get; set; }
    /// <summary>clientId from registration response.</summary>
    public required string ClientId { get; set; }
    /// <summary>clientSecret from registration response. Store in Vault/K8s secret — never in source.</summary>
    public required string ClientSecret { get; set; }
    /// <summary>POST /api/v1/providers/token</summary>
    public required Uri TokenEndpoint { get; set; }
    /// <summary>gRPC Bridge address. Use http:// in dev (plain HTTP/2), https:// in prod.</summary>
    public required Uri BridgeEndpoint { get; set; }
    /// <summary>Semver of this provider implementation. Sent in Hello.version.</summary>
    public string Version { get; set; } = "1.0.0";
    /// <summary>Semaphore cap inside RequestDispatcher — belt-and-suspenders vs RabbitMQ prefetch.</summary>
    public int MaxConcurrentRequests { get; set; } = 8;
    /// <summary>Refresh JWT at this fraction of expiresIn (default 0.80 = 80%).</summary>
    public double TokenRefreshEarlyFraction { get; set; } = 0.80;
    /// <summary>Maximum reconnect backoff after capping. Default 30s (per Phase 9 spec).</summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Jitter fraction applied ±. Default 0.10 = ±10%.</summary>
    public double ReconnectJitterFraction { get; set; } = 0.10;
    /// <summary>Optional HttpHandler override for gRPC channel (e.g. custom TLS, proxy).</summary>
    public HttpMessageHandler? GrpcHttpHandler { get; set; }
    /// <summary>How long Refreshing state waits for in-flight handlers to drain before CancelAll().
    /// Default 30s. Reduce in tests via ProviderSdkOptions.RefreshingDrainTimeout = TimeSpan.FromMilliseconds(100).</summary>
    public TimeSpan RefreshingDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>How long after CancelAll() to wait before force-closing stream (gives handlers time
    /// to observe cancellation and write Terminal). Default 5s. Reduce in tests.</summary>
    public TimeSpan RefreshingForceCloseDelay { get; set; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderId))   throw new ProviderSdkConfigurationException("ProviderSdkOptions.ProviderId is required.");
        if (string.IsNullOrWhiteSpace(ClientId))     throw new ProviderSdkConfigurationException("ProviderSdkOptions.ClientId is required.");
        if (string.IsNullOrWhiteSpace(ClientSecret)) throw new ProviderSdkConfigurationException("ProviderSdkOptions.ClientSecret is required.");
        if (TokenEndpoint is null)                   throw new ProviderSdkConfigurationException("ProviderSdkOptions.TokenEndpoint is required.");
        if (BridgeEndpoint is null)                  throw new ProviderSdkConfigurationException("ProviderSdkOptions.BridgeEndpoint is required.");
        if (MaxConcurrentRequests < 1)               throw new ProviderSdkConfigurationException("ProviderSdkOptions.MaxConcurrentRequests must be >= 1.");
    }
}
