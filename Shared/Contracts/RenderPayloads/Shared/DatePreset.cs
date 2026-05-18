namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record DatePreset
{
    public required string Label { get; init; }

    // "today" | "last_7d" | "this_month" | "this_quarter" | "this_year"
    public required string Value { get; init; }
}
