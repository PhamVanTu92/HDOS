namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record FilterSearchData
{
    public required string FilterKey { get; init; }
    public required string Label { get; init; }
    public required string CurrentValue { get; init; }
    public string? Placeholder { get; init; }
}
