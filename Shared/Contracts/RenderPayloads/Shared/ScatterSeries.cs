namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record ScatterSeries
{
    public required string Name { get; init; }
    public required IReadOnlyList<ScatterPoint> Points { get; init; }
}
