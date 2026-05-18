namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record HeatmapValueRange
{
    public required double Min { get; init; }
    public required double Max { get; init; }
}
