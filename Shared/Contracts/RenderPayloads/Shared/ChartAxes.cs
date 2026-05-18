namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record ChartAxes
{
    public required AxisDefinition X { get; init; }
    public required AxisDefinition Y { get; init; }
    public AxisDefinition? Y2 { get; init; }
}
