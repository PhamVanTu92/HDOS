namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record DateRangeValue
{
    public required string From { get; init; }
    public string? To { get; init; }
}
