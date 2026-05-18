namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record PieSlice
{
    public required string Label { get; init; }
    public required double Value { get; init; }
    public string? Color { get; init; }
}
