using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

// Shared by chartType: "pie_chart", "donut_chart" (compatible group).
public sealed record PieData
{
    public required IReadOnlyList<PieSlice> Slices { get; init; }
    public required double Total { get; init; }
    public string? ValueFormat { get; init; }
}
