namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record FunnelStep
{
    public required string Label { get; init; }
    public required long Value { get; init; }
    public required double PercentOfStart { get; init; }
    public double? DropRate { get; init; }
}
