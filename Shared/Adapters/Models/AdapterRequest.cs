namespace ReportingPlatform.Adapters.Models;

public sealed record AdapterRequest
{
    public required string TenantId { get; init; }
    public required DatasourceDefinition Datasource { get; init; }

    // Dashboard/widget-level filter values (key → JsonElement).
    // Adapters use these to parameterize SQL templates or QueryBuilder filters.
    public required IReadOnlyDictionary<string, JsonElement> Filters { get; init; }

    // Present only for table-type widgets; null for chart/KPI/filter widgets.
    public TablePaginationParams? Pagination { get; init; }
}
