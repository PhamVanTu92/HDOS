using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record FilterDateRangeData
{
    public required string FilterKey { get; init; }
    public required string Label { get; init; }
    public required DateRangeValue CurrentValue { get; init; }
    public required IReadOnlyList<DatePreset> Presets { get; init; }
    public string? MinDate { get; init; }
    public string? MaxDate { get; init; }
}
