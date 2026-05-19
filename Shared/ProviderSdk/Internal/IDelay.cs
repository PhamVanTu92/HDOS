namespace ReportingPlatform.ProviderSdk.Internal;

/// <summary>Abstraction for Task.Delay — injected for testability (tests use instant-mode).</summary>
internal interface IDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

internal sealed class ProductionDelay : IDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
