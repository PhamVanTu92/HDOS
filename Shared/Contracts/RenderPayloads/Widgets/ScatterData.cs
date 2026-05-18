using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record ScatterData
{
    public required IReadOnlyList<ScatterSeries> Series { get; init; }
    public required ChartAxes Axes { get; init; }
}
