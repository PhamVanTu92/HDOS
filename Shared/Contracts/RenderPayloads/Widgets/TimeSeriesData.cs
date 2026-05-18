using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

// Shared by chartType: "line_chart", "bar_chart", "area_chart" (compatible group).
public sealed record TimeSeriesData
{
    public required IReadOnlyList<ChartSeries> Series { get; init; }
    public required ChartAxes Axes { get; init; }
    public IReadOnlyList<ChartAnnotation>? Annotations { get; init; }
}
