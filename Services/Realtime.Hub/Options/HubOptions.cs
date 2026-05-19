namespace ReportingPlatform.RealtimeHub.Options;

public sealed class RealtimeHubOptions
{
    public const string Section = "Hub";

    /// <summary>Maximum simultaneous SignalR connections (global limit).</summary>
    public int MaxConnections { get; init; } = 5_000;

    /// <summary>Hub-level rate limiting — requests per user per minute via Invoke.</summary>
    public int InvokePerUserPerMinute { get; init; } = 100;
}
