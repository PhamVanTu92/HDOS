using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record KpiData
{
    public double? Value { get; init; }
    public required string Format { get; init; }
    public required string Label { get; init; }
    public KpiComparison? Comparison { get; init; }
    public IReadOnlyList<double>? Sparkline { get; init; }
}
