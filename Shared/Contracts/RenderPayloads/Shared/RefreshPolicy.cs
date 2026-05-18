namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record RefreshPolicy
{
    // "interval" | "manual" | "event"
    public required string Mode { get; init; }

    public int? IntervalSeconds { get; init; }
    public int? DebounceMs { get; init; }
}
