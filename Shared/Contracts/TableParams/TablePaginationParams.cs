namespace ReportingPlatform.Contracts.TableParams;

public sealed record TablePaginationParams
{
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public IReadOnlyList<SortSpec>? Sort { get; init; }
    public IReadOnlyList<FilterSpec>? Filters { get; init; }
}
