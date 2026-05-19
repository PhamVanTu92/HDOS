namespace ReportingPlatform.Providers.Models;

public sealed record ProviderRegistration
{
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecretHash { get; init; }
    public string? PendingClientSecretHash { get; init; }
    public DateTimeOffset? PendingSecretExpiresAt { get; init; }
    public required IReadOnlyList<string> Operations { get; init; }
    public required IReadOnlyList<string> ChartTypes { get; init; }
    public required IReadOnlyList<string> Transformers { get; init; }
    public int TimeoutMs { get; init; } = 30_000;
    public required CircuitBreakerConfig CircuitBreaker { get; init; }
    public int Priority { get; init; } = 5;
    public string Status { get; init; } = "active";
    public int MaxConcurrentRequests { get; init; } = 8;
}
