using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record GaugeData
{
    public required double Value { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public string? Unit { get; init; }
    public required IReadOnlyList<GaugeThreshold> Thresholds { get; init; }
    public double? Target { get; init; }
}
