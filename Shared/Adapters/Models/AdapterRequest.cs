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

    // ── External-provider context (null for SQL adapters) ─────────────────

    /// <summary>RequestId of the parent dashboard.render operation — set as CorrelationId on nested request.</summary>
    public string? ParentRequestId { get; init; }

    /// <summary>UserId from the parent operation context — propagated so providers execute under the caller's identity.</summary>
    public string? UserId { get; init; }

    /// <summary>Absolute deadline of the parent operation. Nested timeout = min(config.TimeoutMs, remaining).</summary>
    public DateTimeOffset? ParentDeadline { get; init; }
}
