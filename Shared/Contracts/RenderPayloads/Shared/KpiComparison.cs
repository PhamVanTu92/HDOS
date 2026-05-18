namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record KpiComparison
{
    public required double PreviousValue { get; init; }
    public required double Delta { get; init; }
    public required double DeltaPercent { get; init; }

    // "up" | "down" | "flat"
    public required string Direction { get; init; }

    // Server-side opinion: up is good for revenue, bad for error rate.
    public required bool IsGood { get; init; }

    public required string PeriodLabel { get; init; }
}
