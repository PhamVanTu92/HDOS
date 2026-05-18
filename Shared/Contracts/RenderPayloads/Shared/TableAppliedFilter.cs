namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record TableAppliedFilter
{
    public required string Key { get; init; }
    public required string Op { get; init; }

    // Always a list — single-value ops use a one-element list.
    public required IReadOnlyList<JsonElement> Values { get; init; }
}
