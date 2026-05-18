namespace ReportingPlatform.Contracts.RenderPayloads.Operations;

public sealed record DrillContextResult
{
    public required IReadOnlyDictionary<string, JsonElement> ResolvedFilters { get; init; }
    public required string TargetDashboardCode { get; init; }
    public required bool Valid { get; init; }
}
