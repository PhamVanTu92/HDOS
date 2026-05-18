using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

// Client-side paginated table. Frontend handles all sorting/filtering in memory.
// Max 1000 rows; computed columns are still pre-computed server-side.
public sealed record SimpleTableData
{
    public required IReadOnlyList<TableColumn> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows { get; init; }
    public required TablePagination Pagination { get; init; }
}
