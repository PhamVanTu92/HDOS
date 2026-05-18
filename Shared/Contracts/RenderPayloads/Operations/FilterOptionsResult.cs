using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Operations;

public sealed record FilterOptionsResult
{
    public required string FilterKey { get; init; }
    public required IReadOnlyList<FilterOption> Options { get; init; }
    public required bool HasMore { get; init; }
}
