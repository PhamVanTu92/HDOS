using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record FilterSliderData
{
    public required string FilterKey { get; init; }
    public required string Label { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public required double Step { get; init; }

    // SliderRangeValue when rangeMode=true; scalar (JsonElement) when false.
    public required JsonElement CurrentValue { get; init; }

    public string? Format { get; init; }
    public bool RangeMode { get; init; }
}
