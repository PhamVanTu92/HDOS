namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record HeatmapCell
{
    public required string X { get; init; }
    public required string Y { get; init; }
    public double? Value { get; init; }
    public string? Tooltip { get; init; }
}
