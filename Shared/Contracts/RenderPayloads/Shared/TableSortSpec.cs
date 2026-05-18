namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record TableSortSpec
{
    public required string Key { get; init; }

    // "asc" | "desc"
    public required string Direction { get; init; }
}
