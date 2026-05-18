namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record DrillPathLevel
{
    public required string Level { get; init; }
    public required string Field { get; init; }
    public required string Label { get; init; }
}
