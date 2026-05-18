using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record HeatmapData
{
    public required IReadOnlyList<string> XLabels { get; init; }
    public required IReadOnlyList<string> YLabels { get; init; }

    // Sparse — only non-null intersections are included.
    public required IReadOnlyList<HeatmapCell> Cells { get; init; }

    public required HeatmapValueRange ValueRange { get; init; }

    // "sequential" | "diverging" | "categorical"
    public required string ColorScale { get; init; }
}
