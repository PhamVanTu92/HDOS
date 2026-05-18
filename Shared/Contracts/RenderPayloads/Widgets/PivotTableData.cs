using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record PivotTableData
{
    public required IReadOnlyList<PivotDimension> RowDimensions { get; init; }
    public required IReadOnlyList<PivotDimension> ColumnDimensions { get; init; }
    public required IReadOnlyList<PivotMeasure> Measures { get; init; }
    public required IReadOnlyList<PivotCell> Cells { get; init; }
    public IReadOnlyList<PivotCell>? RowTotals { get; init; }
    public IReadOnlyList<PivotCell>? ColumnTotals { get; init; }
    public IReadOnlyDictionary<string, double?>? GrandTotal { get; init; }
}
