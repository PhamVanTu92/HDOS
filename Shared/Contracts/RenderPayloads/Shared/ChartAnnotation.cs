namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record ChartAnnotation
{
    public required string X { get; init; }
    public required string Label { get; init; }
    public string? Color { get; init; }
}
