namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record PivotDimension
{
    public required string Key { get; init; }
    public required string Label { get; init; }
}
