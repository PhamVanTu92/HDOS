using ReportingPlatform.Contracts.TableParams;

namespace ReportingPlatform.Transformers.Table;

/// <summary>
/// chartType: "simple_table" — client-side pagination, max 1000 rows.
/// VisualConfig key: "columns" — array of <see cref="TableColumn"/> definitions.
/// If omitted, columns are inferred from the first row's keys.
/// </summary>
internal sealed class SimpleTableTransformer : IWidgetTransformer
{
    public string ChartType => "simple_table";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var columns = ParseColumns(ctx.VisualConfig(), rows);

        var result = new SimpleTableData
        {
            Columns    = columns,
            Rows       = rows,
            Pagination = new TablePagination
            {
                Mode      = "client",
                TotalRows = totalRows ?? rows.Count,
            },
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.SimpleTableData));
    }

    private static IReadOnlyList<TableColumn> ParseColumns(
        JsonElement vc,
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows)
    {
        if (vc.ValueKind == JsonValueKind.Object &&
            vc.TryGetProperty("columns", out var colsProp) &&
            colsProp.ValueKind == JsonValueKind.Array)
        {
            return colsProp.EnumerateArray().Select(c => new TableColumn
            {
                Key        = c.TryGetString("key")   ?? string.Empty,
                Label      = c.TryGetString("label")  ?? c.TryGetString("key") ?? string.Empty,
                Type       = c.TryGetString("type")   ?? "string",
                Sortable   = c.TryGetBool("sortable", true),
                Filterable = c.TryGetBool("filterable", false),
            }).ToList();
        }

        // Infer from first row
        if (rows.Count == 0) return [];
        return rows[0].Keys.Select(k => new TableColumn
        {
            Key      = k,
            Label    = k,
            Type     = "string",
            Sortable = true,
        }).ToList();
    }
}
