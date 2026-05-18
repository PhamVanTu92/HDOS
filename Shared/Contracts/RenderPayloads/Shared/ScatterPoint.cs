namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record ScatterPoint
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public double? Size { get; init; }
    public string? Label { get; init; }
    public string? Color { get; init; }
}
