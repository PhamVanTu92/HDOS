using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record FilterDropdownData
{
    public required string FilterKey { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<FilterOption> Options { get; init; }
    public JsonElement? CurrentValue { get; init; }
    public bool MultiSelect { get; init; }
    public bool Searchable { get; init; }
    public bool Clearable { get; init; }
}
