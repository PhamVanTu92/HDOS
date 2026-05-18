namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record GaugeThreshold
{
    public required double From { get; init; }
    public required double To { get; init; }
    public required string Color { get; init; }
    public required string Label { get; init; }
}
