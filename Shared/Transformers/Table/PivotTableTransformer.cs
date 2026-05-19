namespace ReportingPlatform.Transformers.Table;

/// <summary>
/// chartType: "pivot_table" — cross-tabulates rows into a pivot table.
/// VisualConfig keys:
///   rowDimensions: [{key, label}]
///   columnDimensions: [{key, label}]
///   measures: [{key, label, aggregate ("sum"|"count"|"avg"|"min"|"max"), format?}]
///   showRowTotals (bool, default true), showColumnTotals (bool, default true)
/// </summary>
internal sealed class PivotTableTransformer : IWidgetTransformer
{
    public string ChartType => "pivot_table";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc = ctx.VisualConfig();

        var rowDims  = ParseDimensions(vc, "rowDimensions");
        var colDims  = ParseDimensions(vc, "columnDimensions");
        var measures = ParseMeasures(vc);

        var showRowTotals = vc.TryGetBool("showRowTotals", true);
        var showColTotals = vc.TryGetBool("showColumnTotals", true);

        // Group rows by composite row-key and column-key
        var rowGroups = rows.GroupBy(r =>
            rowDims.Select(d => r.GetRowValue(d.Key).ToStringValue() ?? string.Empty).ToList(),
            ListEqualityComparer.Instance);

        var colGroups = rows.GroupBy(r =>
            colDims.Select(d => r.GetRowValue(d.Key).ToStringValue() ?? string.Empty).ToList(),
            ListEqualityComparer.Instance);

        // All unique row keys and column keys
        var allRowKeys = rowGroups.Select(g => g.Key).ToList();
        var allColKeys = colGroups.Select(g => g.Key).ToList();

        // Build cells
        var cells = new List<PivotCell>();
        foreach (var rowKey in allRowKeys)
        {
            foreach (var colKey in allColKeys)
            {
                var matchingRows = rows.Where(r =>
                    rowDims.Select(d => r.GetRowValue(d.Key).ToStringValue() ?? string.Empty)
                           .SequenceEqual(rowKey) &&
                    colDims.Select(d => r.GetRowValue(d.Key).ToStringValue() ?? string.Empty)
                           .SequenceEqual(colKey)).ToList();

                if (matchingRows.Count == 0) continue;

                var values = measures.ToDictionary(
                    m => m.Key,
                    m => Aggregate(matchingRows, m),
                    StringComparer.Ordinal);

                cells.Add(new PivotCell
                {
                    RowKey    = rowKey,
                    ColumnKey = colKey,
                    Values    = values,
                });
            }
        }

        var result = new PivotTableData
        {
            RowDimensions    = rowDims,
            ColumnDimensions = colDims,
            Measures         = measures,
            Cells            = cells,
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.PivotTableData));
    }

    private static double? Aggregate(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        PivotMeasure measure)
    {
        var nums = rows.Select(r => r.GetRowValue(measure.Key).ToDouble())
                       .Where(v => v is not null)
                       .Select(v => v!.Value)
                       .ToList();

        if (nums.Count == 0) return null;

        return measure.Aggregate switch
        {
            "count" => (double)rows.Count,
            "sum"   => nums.Sum(),
            "avg"   => nums.Average(),
            "min"   => nums.Min(),
            "max"   => nums.Max(),
            _       => nums.Sum(),
        };
    }

    private static IReadOnlyList<PivotDimension> ParseDimensions(JsonElement vc, string prop)
    {
        if (vc.ValueKind != JsonValueKind.Object ||
            !vc.TryGetProperty(prop, out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray().Select(el => new PivotDimension
        {
            Key   = el.TryGetString("key")   ?? string.Empty,
            Label = el.TryGetString("label") ?? el.TryGetString("key") ?? string.Empty,
        }).ToList();
    }

    private static IReadOnlyList<PivotMeasure> ParseMeasures(JsonElement vc)
    {
        if (vc.ValueKind != JsonValueKind.Object ||
            !vc.TryGetProperty("measures", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray().Select(el => new PivotMeasure
        {
            Key       = el.TryGetString("key")       ?? string.Empty,
            Label     = el.TryGetString("label")     ?? el.TryGetString("key") ?? string.Empty,
            Aggregate = el.TryGetString("aggregate") ?? "sum",
            Format    = el.TryGetString("format"),
        }).ToList();
    }

    // Custom equality comparer for List<string> used as dictionary keys
    private sealed class ListEqualityComparer : IEqualityComparer<List<string>>
    {
        internal static readonly ListEqualityComparer Instance = new();

        public bool Equals(List<string>? x, List<string>? y) =>
            x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(List<string> obj) =>
            obj.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal)));
    }
}
