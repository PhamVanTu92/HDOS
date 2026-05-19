using ReportingPlatform.Contracts.TableParams;

namespace ReportingPlatform.Transformers.Table;

/// <summary>
/// chartType: "advanced_table" — server-side paginated table.
/// Echoes back applied sort and filter state from the request context.
/// When <paramref name="totalRows"/> is null, pagination is disabled and
/// TotalRows is set to the row count (see §12.2 note).
/// </summary>
internal sealed class AdvancedTableTransformer : IWidgetTransformer
{
    public string ChartType => "advanced_table";

    public Task<JsonElement> TransformAsync(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows,
        long? totalRows,
        WidgetRenderContext ctx,
        CancellationToken ct = default)
    {
        var vc = ctx.VisualConfig();
        var columns = ParseColumns(vc, rows);

        // Determine pagination mode based on whether totalRows was provided
        var paginationDisabled = totalRows is null;
        var effectiveTotalRows = totalRows ?? rows.Count;

        // Infer current page/size from filters context if present
        int? page     = null;
        int? pageSize = null;
        int? totalPages = null;

        if (ctx.Filters.TryGetValue("_page", out var pageEl) &&
            pageEl.TryGetInt32(out var p))
            page = p;

        if (ctx.Filters.TryGetValue("_page_size", out var sizeEl) &&
            sizeEl.TryGetInt32(out var ps))
            pageSize = ps;

        if (page.HasValue && pageSize.HasValue && pageSize.Value > 0)
            totalPages = (int)Math.Ceiling((double)effectiveTotalRows / pageSize.Value);

        var result = new AdvancedTableData
        {
            Columns    = columns,
            Rows       = rows,
            Pagination = new TablePagination
            {
                Mode       = paginationDisabled ? "client" : "server",
                Page       = page,
                PageSize   = pageSize,
                TotalRows  = effectiveTotalRows,
                TotalPages = totalPages,
            },
        };

        return Task.FromResult(
            JsonSerializer.SerializeToElement(result, TransformersJsonContext.Default.AdvancedTableData));
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
                Sortable   = c.TryGetBool("sortable",   true),
                Filterable = c.TryGetBool("filterable", false),
                FilterType = c.TryGetString("filterType"),
                Format     = c.TryGetString("format"),
                Computed   = c.TryGetString("computed"),
                ComputedOn = c.TryGetString("computedOn"),
                Aggregation= c.TryGetString("aggregation"),
                Align      = c.TryGetString("align"),
                Frozen     = c.TryGetString("frozen"),
                Visible    = c.TryGetBool("visible", true),
                Width      = vc.TryGetDouble("width") is { } w ? (int)w : null,
            }).ToList();
        }

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
