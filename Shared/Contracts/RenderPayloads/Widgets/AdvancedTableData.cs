using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

// Server-side paginated table. Always echoes back applied sort and filters.
// Kept separate from SimpleTableData — the two have different required fields
// and merging them would introduce false optionality (see PHASE_2_PLAN.md).
public sealed record AdvancedTableData
{
    public required IReadOnlyList<TableColumn> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows { get; init; }
    public required TablePagination Pagination { get; init; }
    public IReadOnlyList<TableSortSpec>? Sort { get; init; }
    public IReadOnlyList<TableAppliedFilter>? Filters { get; init; }
    public TableFooter? Footer { get; init; }
    public IReadOnlyList<string>? ExportHint { get; init; }
}
