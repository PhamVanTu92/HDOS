namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record ChartSeries
{
    public required string Name { get; init; }
    public required IReadOnlyList<SeriesPoint> Data { get; init; }
}
