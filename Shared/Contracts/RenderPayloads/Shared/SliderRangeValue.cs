namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record SliderRangeValue
{
    public required double From { get; init; }
    public required double To { get; init; }
}
