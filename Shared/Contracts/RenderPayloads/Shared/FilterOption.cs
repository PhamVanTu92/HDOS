namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record FilterOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public long? Count { get; init; }
}
