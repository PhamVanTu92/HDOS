namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record TablePagination
{
    // "server" | "client"
    public required string Mode { get; init; }

    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public required long TotalRows { get; init; }
    public int? TotalPages { get; init; }
}
