namespace ReportingPlatform.Providers.Models;

public sealed record CircuitBreakerConfig
{
    public int FailureThreshold { get; init; } = 5;
    public int WindowSeconds { get; init; } = 60;
    public int CooldownSeconds { get; init; } = 30;
}
