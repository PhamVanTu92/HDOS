# PHASE_4_PLAN.md — Resolver Core, Adapters & 19 Widget Transformers
> Status: APPROVED | Author: Claude Sonnet 4.6 | Date: 2026-05-19

Phase 4 builds the data-fetch and render pipeline: four new Shared projects that take a `DashboardDefinition`, fan out to datasources, apply computed column transforms, invoke chart-type transformers, and assemble `DashboardRenderPayload`. It is the highest-complexity phase in the plan.

---

## 0. CVE scan (pre-phase gate)

```
dotnet list package --vulnerable --include-transitive
```

Run before writing any code. Result at plan time: 0 vulnerable packages. ✓

---

## 1. Build order and dependency graph

```
Shared/Contracts        (Phase 2 — done)
Shared/Providers        (Phase 3 — done)
Shared/Caching          (Phase 2 — done)
         ↓
Shared/QueryBuilder     (Phase 4 — step 1)
         ↓
Shared/Adapters         (Phase 4 — step 2: depends on QueryBuilder + Providers)
         ↓
Shared/Transformers     (Phase 4 — step 3: depends on Contracts only)
         ↓
Shared/Resolver         (Phase 4 — step 4: depends on all above + Caching)
         ↓
Services/Gateway        (Phase 5 — submits dashboard.render)
Services/Worker         (Phase 6 — resolves via Resolver)
```

Build order within Phase 4:
1. Migration V004 (`queryable_sources` table)
2. `Shared/QueryBuilder/` — whitelist store + SqlKata wrapper
3. `Shared/Adapters/` — IDatasourceAdapter + 3 implementations
4. `Shared/Transformers/` — IWidgetTransformer + 19 implementations + ComputedColumnEngine
5. `Shared/Resolver/` — IDashboardResolver + widget validators + cache integration
6. Tests: `tests/QueryBuilder.Tests/`, `tests/Transformers.Tests/`, `tests/Resolver.Tests/`

---

## 2. Project structures

### 2.1 `Shared/QueryBuilder/`

```
Shared/QueryBuilder/
├── QueryBuilder.csproj
├── GlobalUsings.cs
├── Whitelist/
│   ├── IQueryableSourceRepository.cs   ← reads queryable_sources from Postgres
│   ├── PostgresQueryableSourceRepository.cs
│   └── QueryableSource.cs              ← in-memory model
├── Builder/
│   ├── SqlKataQueryBuilder.cs          ← wraps SqlKata; applies whitelist
│   └── TableParamsApplicator.cs        ← injects pagination/sort/filter to Query
└── Extensions/
    └── QueryBuilderExtensions.cs       ← AddPlatformQueryBuilder()
```

### 2.2 `Shared/Adapters/`

```
Shared/Adapters/
├── Adapters.csproj
├── GlobalUsings.cs
├── Abstractions/
│   ├── IDatasourceAdapter.cs
│   ├── AdapterRequest.cs
│   ├── AdapterResult.cs
│   └── ColumnDescriptor.cs
├── Config/
│   └── DatasourceConfig.cs             ← deserialized from DatasourceDefinition.ConnectionConfig
├── Implementations/
│   ├── SqlQueryBuilderAdapter.cs       ← "sql" + mode:"query_builder"
│   ├── SqlRawAdapter.cs                ← "sql" + mode:"raw_sql"
│   └── TimescaleAdapter.cs             ← "sql" + mode:"timescale"
├── Factory/
│   └── DatasourceAdapterFactory.cs     ← resolves correct adapter by DatasourceConfig.Mode
└── Extensions/
    └── AdaptersExtensions.cs           ← AddPlatformAdapters()
```

### 2.3 `Shared/Transformers/`

```
Shared/Transformers/
├── Transformers.csproj
├── GlobalUsings.cs
├── Abstractions/
│   └── IWidgetTransformer.cs
├── Engine/
│   ├── IComputedColumnEngine.cs
│   └── ComputedColumnEngine.cs         ← 7 built-in transforms
├── Visualization/                      ← 10 chart types
│   ├── LineChartTransformer.cs
│   ├── BarChartTransformer.cs
│   ├── AreaChartTransformer.cs
│   ├── PieChartTransformer.cs
│   ├── DonutChartTransformer.cs
│   ├── KpiTransformer.cs
│   ├── GaugeTransformer.cs
│   ├── HeatmapTransformer.cs
│   ├── ScatterTransformer.cs
│   └── FunnelTransformer.cs
├── Tables/                             ← 3 table types
│   ├── SimpleTableTransformer.cs
│   ├── AdvancedTableTransformer.cs
│   └── PivotTableTransformer.cs
├── Filters/                            ← 4 filter types
│   ├── FilterDropdownTransformer.cs
│   ├── FilterDateRangeTransformer.cs
│   ├── FilterSliderTransformer.cs
│   └── FilterSearchTransformer.cs
├── Layout/                             ← 2 layout types
│   ├── TextWidgetTransformer.cs
│   └── TabContainerTransformer.cs
└── Extensions/
    └── TransformersExtensions.cs       ← AddPlatformTransformers()
```

### 2.4 `Shared/Resolver/`

```
Shared/Resolver/
├── Resolver.csproj
├── GlobalUsings.cs
├── Abstractions/
│   └── IDashboardResolver.cs
├── Validation/
│   ├── IWidgetDefinitionValidator.cs
│   └── WidgetDefinitionValidator.cs    ← 9 rules; used by metadata.dashboard.upsert
├── Cache/
│   └── WidgetCacheService.cs           ← L0 (in-memory) + L1 (Redis) lookup/write
├── Core/
│   └── DashboardResolver.cs            ← fan-out + semaphore + per-widget error isolation
└── Extensions/
    └── ResolverExtensions.cs           ← AddPlatformResolver()
```

---

## 3. Adapter interface design (Q2)

### 3.1 `IDatasourceAdapter`

```csharp
namespace ReportingPlatform.Adapters.Abstractions;

public interface IDatasourceAdapter
{
    // "query_builder" | "raw_sql" | "timescale"
    string Mode { get; }

    Task<AdapterResult> ExecuteAsync(AdapterRequest request, CancellationToken ct = default);
}
```

### 3.2 `AdapterRequest` and `AdapterResult`

```csharp
public sealed record AdapterRequest
{
    public required string TenantId { get; init; }
    public required DatasourceConfig Config { get; init; }
    public required JsonElement Params { get; init; }
    public TableParams? Table { get; init; }     // null = no pagination required
    public int TimeoutMs { get; init; } = 30_000;
}

public sealed record AdapterResult
{
    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }
    public int? TotalCount { get; init; }         // set when Table != null
    public IReadOnlyList<ColumnDescriptor>? Columns { get; init; }
    public TimeSpan ExecutionTime { get; init; }
}

public sealed record ColumnDescriptor
{
    public required string Name { get; init; }
    // Npgsql NpgsqlDbType name, e.g. "text", "int4", "float8", "timestamptz", "bool"
    public required string DbType { get; init; }
}
```

### 3.3 `DatasourceConfig`

`DatasourceDefinition.ConnectionConfig` (stored as JSONB in `report_definitions`) is deserialized into a single flat record. The `Mode` field discriminates at runtime which adapter handles the request. No polymorphic JSON hierarchy is needed because each adapter only reads the fields relevant to it.

```csharp
public sealed record DatasourceConfig
{
    // "query_builder" | "raw_sql" | "timescale"
    public required string Mode { get; init; }

    // query_builder + timescale: references queryable_sources.source_name
    public string? Source { get; init; }

    // query_builder: subset of allowed_columns to SELECT; null = all allowed columns
    public IReadOnlyList<string>? Select { get; init; }

    // query_builder + timescale: default ordering applied when _table.sortBy is absent
    public string? DefaultSort { get; init; }
    public string? DefaultSortDir { get; init; }  // "asc" | "desc", default "asc"

    // raw_sql: parameterized SQL template with optional {{_pagination}}, {{_orderBy}}, {{_extraFilters}}
    public string? Template { get; init; }
    // raw_sql with pagination: COUNT query to determine TotalCount
    public string? CountTemplate { get; init; }

    // timescale: time-series specific
    public string? TimeColumn { get; init; }
    public string? Interval { get; init; }         // "1m" | "5m" | "1h" | "1d" | "1w"
    public IReadOnlyList<TimescaleAggregation>? Aggregations { get; init; }

    // Named DB connection key (maps to connection string in IConfiguration["Connections:{name}"])
    // Defaults to "default" when absent.
    public string Connection { get; init; } = "default";
}

public sealed record TimescaleAggregation
{
    public required string Column { get; init; }
    // "avg" | "sum" | "min" | "max" | "count" | "last"
    public required string Function { get; init; }
    public string? Alias { get; init; }
}
```

### 3.4 Adapter dispatch — `DatasourceAdapterFactory`

```csharp
public sealed class DatasourceAdapterFactory
{
    private readonly IReadOnlyDictionary<string, IDatasourceAdapter> _adapters;

    public DatasourceAdapterFactory(IEnumerable<IDatasourceAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(a => a.Mode, StringComparer.OrdinalIgnoreCase);
    }

    public IDatasourceAdapter Resolve(string mode) =>
        _adapters.TryGetValue(mode, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No adapter registered for mode '{mode}'");
}
```

All three adapters are registered in DI; the factory dispatches by `DatasourceConfig.Mode`.

### 3.5 Adapter matrix

| Mode | Adapter class | Datasource | Pagination |
|---|---|---|---|
| `query_builder` | `SqlQueryBuilderAdapter` | PostgreSQL via SqlKata | ✓ (LIMIT/OFFSET) |
| `raw_sql` | `SqlRawAdapter` | PostgreSQL via Npgsql | ✓ (via `{{_pagination}}`) |
| `timescale` | `TimescaleAdapter` | TimescaleDB (PostgreSQL wire) | ✗ (time-series; no LIMIT) |
| `grpc` | (Phase 10) | External provider | — |

`TimescaleAdapter` inherits internal connection-management helpers from `SqlQueryBuilderAdapter`; it does NOT extend via class inheritance (composition over inheritance).

---

## 4. Whitelist mechanism + migration V004 (Q3)

### 4.1 `queryable_sources` table — migration V004

```sql
-- db/Migrations/V004__create_queryable_sources.sql
CREATE TABLE IF NOT EXISTS queryable_sources (
    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id        TEXT        NOT NULL,
    source_name      TEXT        NOT NULL,   -- stable slug, matches DatasourceConfig.Source
    schema_name      TEXT        NOT NULL DEFAULT 'public',
    table_name       TEXT        NOT NULL,
    -- JSON array of allowed column names; empty array means ALL columns allowed
    allowed_columns  JSONB       NOT NULL DEFAULT '[]',
    sortable_columns JSONB       NOT NULL DEFAULT '[]',  -- subset that can be ORDER BY'd
    max_rows         INT         NOT NULL DEFAULT 10000, -- hard cap on LIMIT
    status           TEXT        NOT NULL DEFAULT 'active'
                         CHECK (status IN ('active', 'disabled')),
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_tenant_source UNIQUE (tenant_id, source_name)
);

CREATE INDEX idx_qs_tenant ON queryable_sources (tenant_id, status);

CREATE TRIGGER trg_qs_updated_at
    BEFORE UPDATE ON queryable_sources
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
```

### 4.2 `QueryableSource` in-memory model

```csharp
public sealed record QueryableSource
{
    public required string TenantId { get; init; }
    public required string SourceName { get; init; }
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    // Empty = all columns allowed
    public required IReadOnlyList<string> AllowedColumns { get; init; }
    public required IReadOnlyList<string> SortableColumns { get; init; }
    public int MaxRows { get; init; } = 10_000;
}
```

### 4.3 Whitelist runtime checks (in `SqlQueryBuilderAdapter`)

Before SqlKata builds the query, in order:

1. **Source exists**: `IQueryableSourceRepository.GetAsync(tenantId, sourceName)` — if null, throw `AdapterException("SOURCE_NOT_FOUND")`
2. **Column whitelist** (if `allowed_columns` is non-empty):
   - Each column in `DatasourceConfig.Select` must be in `AllowedColumns`; reject with `AdapterException("COLUMN_NOT_ALLOWED", colName)`
   - Wildcards (`SELECT *`) are rejected if `AllowedColumns` is non-empty
3. **Sort column whitelist**: `_table.sortBy` must be in `SortableColumns` (or `AllowedColumns` if `SortableColumns` is empty); reject with `AdapterException("SORT_NOT_ALLOWED")`
4. **Filter column whitelist**: each filter key in `_table.filters` must be in `AllowedColumns`
5. **Max rows cap**: `_table.pageSize` capped to `min(requested, MaxRows)`

SQL injection is structurally impossible — SqlKata parameterizes all values; column names are validated against the whitelist before being passed to SqlKata.

### 4.4 `IQueryableSourceRepository` caching

The repository caches results in-memory (TTL 60s) to avoid per-request Postgres reads. Cache is invalidated on `queryable_sources` write via Redis pub/sub channel `cache-invalidate:queryable-sources:{tenantId}`.

---

## 5. Server-side table protocol (Q4)

### 5.1 `TableParams` record

```csharp
public sealed record TableParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? SortBy { get; init; }
    public string SortDir { get; init; } = "asc";   // "asc" | "desc"
    public IReadOnlyList<TableFilter>? Filters { get; init; }
}

public sealed record TableFilter
{
    public required string Column { get; init; }
    // "eq" | "neq" | "gt" | "gte" | "lt" | "lte" | "like" | "in" | "is_null" | "not_null"
    public required string Op { get; init; }
    public JsonElement Value { get; init; }  // scalar or array for "in"
}
```

`TableParams` is parsed from `RequestEnvelope.Params["_table"]` by the resolver before invoking the adapter. It is absent for non-table widget types.

**Q1 answer — default `_table` when request omits it (Option B)**:

If `RequestEnvelope.Params["_table"]` is absent for an `advanced_table` widget, the resolver initializes `TableParams` with defaults:
```csharp
var tableParams = params.TryGetProperty("_table", out var t)
    ? t.Deserialize(ResolverJsonContext.Default.TableParams)
    : new TableParams { Page = 1, PageSize = 25 };  // Option B: backend default
```
The adapter applies `LIMIT 25 OFFSET 0`. The frontend receives a valid first page on initial render and can subsequently send explicit `_table` params for sorting, pagination, and filtering. The `TotalCount` in the response always reflects the full unfiltered row count.

### 5.2 QueryBuilder adapter — pagination and filtering

`SqlKataQueryBuilder` applies `TableParams` via `TableParamsApplicator`:

```csharp
// TableParamsApplicator.Apply(Query q, TableParams t, QueryableSource src)
q.Limit(Math.Min(t.PageSize, src.MaxRows))
 .Offset((t.Page - 1) * t.PageSize);

if (t.SortBy is not null)  // whitelist already validated
    q.OrderByRaw(t.SortDir == "desc" ? $"\"{t.SortBy}\" DESC" : $"\"{t.SortBy}\"");

foreach (var f in t.Filters ?? [])
    ApplyFilter(q, f);  // translates Op to SqlKata .Where / .WhereRaw
```

`TotalCount` is fetched via a separate `SELECT COUNT(*)` query (same filters, no pagination).

### 5.3 Raw SQL adapter — template substitution

`SqlRawAdapter` replaces three placeholders in the `Template` string:

| Placeholder | Substituted with | When |
|---|---|---|
| `{{_pagination}}` | `LIMIT {n} OFFSET {m}` | Always when `TableParams != null` |
| `{{_orderBy}}` | `ORDER BY "{col}" {dir}` | When `_table.sortBy` present and whitelisted |
| `{{_extraFilters}}` | `AND col op $N ...` | When `_table.filters` non-empty |

**Injection safety**: placeholder replacement is NOT string interpolation — values are Npgsql parameters (`$1`, `$2`, …). Column names from `_table.sortBy` and `_table.filters[].column` are validated against a runtime whitelist (same `queryable_sources.sortable_columns` + `allowed_columns`) before being embedded in SQL as quoted identifiers.

If `Template` contains `{{_pagination}}` but `TableParams` is null, the placeholder is removed (replaced with empty string). If `Template` lacks `{{_pagination}}` and `TableParams != null`, this is a configuration error logged as a warning — adapter returns rows without pagination limit.

---

## 6. Computed column engine (Q5)

### 6.1 Interface

```csharp
namespace ReportingPlatform.Transformers.Engine;

public interface IComputedColumnEngine
{
    // Applies computed columns in-place on the row set.
    // partitionBy: optional column name to scope running/rank calculations.
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Apply(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<ComputedColumnSpec> specs);
}

public sealed record ComputedColumnSpec
{
    public required string OutputColumn { get; init; }   // name of the new/replaced column
    public required string SourceColumn { get; init; }   // input column
    public required string Transform { get; init; }      // one of 7 names below
    public string? PartitionBy { get; init; }            // for rank/running calcs
}
```

### 6.2 Seven pre-defined transforms

| Transform name | Description | Output type |
|---|---|---|
| `percent_of_total` | `value / SUM(sourceCol) * 100` over full row set (or partition) | `double` |
| `running_total` | Cumulative sum of `sourceCol` in row order (within partition if set) | same as source |
| `rank_asc` | 1-based rank ascending within partition; ties share lowest rank | `int` |
| `rank_desc` | 1-based rank descending within partition | `int` |
| `mom_change` | `(current - prev) / prev * 100` — Month-over-Month %; requires `sourceCol` to be ordered by time | `double?` (null on first row) |
| `yoy_change` | `(current - same_period_prior_year) / prior * 100` — requires time column in scope | `double?` |
| `z_score` | `(value - MEAN) / STDDEV` over row set | `double` |

**Execution point**: `ComputedColumnEngine.Apply()` is called by the resolver AFTER the adapter returns rows and BEFORE the transformer executes. The engine returns a new row list (immutable output); the original rows are not mutated.

**Configuration**: `ComputedColumnSpec[]` is read from `WidgetDefinition.VisualConfig["computedColumns"]` (a JsonElement parsed at resolve time). If absent or empty, the engine is skipped.

**Ordering invariant**: `mom_change` and `yoy_change` assume rows are in temporal order as returned by the adapter. It is the widget author's responsibility to ensure `defaultSort` is set to the time column in `DatasourceConfig`.

---

## 7. Resolver concurrency (Q6)

### 7.1 Chosen approach: pre-fetch phase → `SemaphoreSlim` + `Task.WhenAll`

**Pre-fetch phase** (before main fan-out):

`DashboardResolver` scans all `filter_dropdown` widgets in the dashboard definition BEFORE starting the main widget fan-out:
1. Collect all `(widgetId, datasourceId, optionsSource config)` tuples from `filter_dropdown` widgets that have `optionsSource` set
2. Deduplicate by canonical key `{tenantId}:{datasourceId}:{optionsSource.source}` — multiple dropdowns pointing to the same source share one fetch
3. Fetch each unique source's options rows under the same `_widgetSemaphore` (respects connection pool budget)
4. Build `IReadOnlyDictionary<string, IReadOnlyList<FilterOption>>` keyed by `widgetId`
5. Package into `WidgetRenderContext` passed to every transformer during fan-out

This ensures all DB I/O is under semaphore control, tracing has a clear pre-fetch span before the fan-out span, and `FilterDropdownTransformer` needs no DI adapter dependency.

**Main fan-out phase (after pre-fetch):**

```csharp
// DashboardResolver.cs
private readonly SemaphoreSlim _widgetSemaphore;

// Injected via config: Resolver:MaxConcurrentWidgets (default 8)
public DashboardResolver(..., IOptions<ResolverOptions> options, ...)
{
    _widgetSemaphore = new SemaphoreSlim(options.Value.MaxConcurrentWidgets);
}
```

Fan-out pattern:
```csharp
var widgetTasks = definition.Widgets!
    .Select(w => RenderWidgetAsync(w, definition, filters, tableParams, ct))
    .ToList();

var envelopes = await Task.WhenAll(widgetTasks);
```

Per-widget execution with isolation:
```csharp
private async Task<WidgetEnvelope> RenderWidgetAsync(
    WidgetDefinition widget, ...)
{
    await _widgetSemaphore.WaitAsync(ct);
    try
    {
        return await RenderWidgetCoreAsync(widget, ...);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Widget {WidgetId} render failed", widget.WidgetId);
        return new WidgetEnvelope
        {
            WidgetId = widget.WidgetId,
            ChartType = widget.ChartType,
            Error = new WidgetError
            {
                Code    = ErrorCodes.InternalError,
                Message = "Widget render failed",
            },
        };
    }
    finally
    {
        _widgetSemaphore.Release();
    }
}
```

**Why `Task.WhenAll` + `SemaphoreSlim` over `Parallel.ForEachAsync`**:
- Per-widget error isolation is explicit — one widget throwing never cancels others
- `MaxConcurrentWidgets` is set by ops config, not CPU count
- Backpressure is visible and tunable
- `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` works similarly but mixes CPU and IO parallelism assumptions

### 7.2 Cache integration

`DashboardResolver.RenderWidgetCoreAsync` checks cache before adapter execution:

```
1. Compute cache key: "widget:{tenantId}:{dashCode}:v{version}:{widgetId}:{filtersHash}"
   filtersHash = SHA256(canonicalized JSON of active filters)[0..8] (base64url)
2. WidgetCacheService.TryGetAsync(key) → WidgetEnvelope?
3. If hit: return cached envelope (skip adapter + transform)
4. On miss: execute adapter → compute → transform → serialize
5. If DatasourceDefinition.CacheSeconds > 0: cache result with TTL
```

`WidgetCacheService` is a two-level cache:
- **L0**: `MemoryCache` (in-process, evicted on version bump via pub/sub)
- **L1**: Redis (shared across replicas), TTL = `DatasourceDefinition.CacheSeconds`

L0 is checked first (sub-millisecond). L1 is checked on L0 miss (network RTT). On L1 hit, the result is promoted to L0.

### 7.2.1 Filter canonicalization for cache key

Cache key: `widget:{tenantId}:{dashCode}:v{version}:{widgetId}:{filtersHash}`

`filtersHash = Base64UrlEncode(SHA256(canonicalFiltersJson))[..8]`

`FilterCanonicalizer` in `Shared/Resolver/Cache/FilterCanonicalizer.cs` applies these 6 rules in order to produce a deterministic JSON string:

| # | Rule | Example |
|---|---|---|
| 1 | **Keys sorted alphabetically** (ascending, ordinal) | `{"a":1,"b":2}` not `{"b":2,"a":1}` |
| 2 | **Null and missing values omitted** | a filter with `null` value is not included in hash input |
| 3 | **Compact serialization** — no whitespace, no indentation | `{"date":"2026-01-01","status":"active"}` |
| 4 | **String values are case-preserved** — no normalization | `"Active"` and `"active"` produce different hashes (correct: filter values are case-sensitive) |
| 5 | **Array values: elements sorted alphabetically** — multi-select filter values sorted before hashing | `["b","a"]` → `["a","b"]` in canonical form |
| 6 | **Numeric values: standard JSON decimal** — no scientific notation for values in `[1e-6, 1e15]`; trailing zeros stripped | `1.50` → `1.5`, `1000000` → `1000000` (not `1e6`) |

Implementation: `FilterCanonicalizer.Canonicalize(IReadOnlyDictionary<string, JsonElement> filters)` returns a `string`. SHA256 is computed via `System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical))`. The 8-char base64url suffix provides ~48 bits of collision resistance — sufficient for per-tenant cache key disambiguation.

### 7.3 `IDashboardResolver` interface

```csharp
namespace ReportingPlatform.Resolver.Abstractions;

public interface IDashboardResolver
{
    Task<DashboardRenderPayload> RenderAsync(
        string tenantId,
        string dashboardCode,
        IReadOnlyDictionary<string, JsonElement> filters,
        TableParams? tableParams,
        CancellationToken ct = default);
}
```

### 7.4 `ResolverOptions`

```csharp
public sealed class ResolverOptions
{
    public int MaxConcurrentWidgets { get; set; } = 8;
    // Timeout applied per-widget (overrides DatasourceDefinition.CacheSeconds TTL check)
    public int WidgetTimeoutMs { get; set; } = 30_000;
}
```

---

## 8. Widget definition validator architecture (Q7)

### 8.1 Interface

```csharp
namespace ReportingPlatform.Resolver.Validation;

public interface IWidgetDefinitionValidator
{
    // Called by metadata.dashboard.upsert before persisting.
    // Returns aggregate validation result across all widgets in the dashboard.
    Task<ValidationResult> ValidateAsync(
        DashboardDefinition definition,
        string tenantId,
        CancellationToken ct = default);
}
```

`WidgetDefinitionValidator` is `internal sealed class` registered in DI. External callers use the interface.

### 8.2 Nine validation rules (per §13.5)

| # | Rule | Error code | Notes |
|---|---|---|---|
| R1 | `chartType` must be one of the 20 registered transformer chart types | `UNKNOWN_CHART_TYPE` | Checked against `ITransformerRegistry.SupportedTypes` |
| R2 | `datasourceId` must reference a `DatasourceDefinition` that exists in the store | `DATASOURCE_NOT_FOUND` | Async lookup via `IDatasourceDefinitionRepository` |
| R3 | For `DatasourceDefinition.Type = "sql"` with `mode = "query_builder"`: `DatasourceConfig.Source` must exist in `queryable_sources` for the tenant | `SOURCE_NOT_FOUND` | Async lookup via `IQueryableSourceRepository` |
| R4 | For `chartType = "advanced_table"`: datasource must support pagination — `Type = "sql"` and `mode` must be `"query_builder"` or `"raw_sql"` (not `"timescale"`, not `"grpc"`) | `PAGINATION_NOT_SUPPORTED` | See §11 |
| R5 | `timeout_ms` (from `VisualConfig["timeoutMs"]` if set) must be in range `[1000, 300_000]` | `INVALID_TIMEOUT` | Defaults to `ResolverOptions.WidgetTimeoutMs` if absent |
| R6 | For `chartType = "gauge"`: `VisualConfig` must contain `min` (number), `max` (number), and `max > min` | `INVALID_GAUGE_CONFIG` | |
| R7 | For `chartType = "filter_dropdown"` with `optionsSource` set: `optionsSource.source` must exist in `queryable_sources` for the tenant | `OPTIONS_SOURCE_NOT_FOUND` | |
| R8 | `VisualConfig["computedColumns"]` (if present) — each entry must have valid `sourceColumn` (must be in `DatasourceConfig.Select` or `AllowedColumns`), valid `transform` (one of 7 names), and `outputColumn` must not shadow reserved columns. **Enhanced**: when `transform` is `mom_change` or `yoy_change`, `DatasourceConfig.DefaultSort` MUST be set and `DefaultSortDir` MUST be `"asc"` — these transforms require ascending temporal ordering. Rejection error for the enhanced check: `MISSING_TIME_ORDER_FOR_TIME_TRANSFORM`. | `INVALID_COMPUTED_COLUMN` / `MISSING_TIME_ORDER_FOR_TIME_TRANSFORM` | Only checked for sql-backed widgets |
| R9 | `WidgetId` must be unique within the dashboard definition; duplicates are rejected | `DUPLICATE_WIDGET_ID` | Pure in-memory check; O(n) |

Rules R3, R4, R7, R8 are only evaluated when `DatasourceDefinition.Type = "sql"`. Rules for external provider widgets (`"grpc"`) are validated against `IOperationRegistry` instead (deferred to Phase 8).

### 8.3 Wiring into `metadata.dashboard.upsert`

The `metadata.dashboard.upsert` handler (Phase 5) calls:
```csharp
var result = await _validator.ValidateAsync(definition, tenantId, ct);
if (!result.IsValid)
    return ProblemResult(422, result.Errors);
// Then: atomically increment version, write to report_definitions, publish cache-invalidate
```

---

## 9. Dashboard version cache invalidation (Q8)

### 9.1 Redis pub/sub channel and payload

```
Channel: "cache-invalidate:dashboard:{dashboardCode}"
Message payload (JSON):
{
  "code":     "sales-overview",
  "version":  42,
  "tenantId": "acme"
}
```

One channel per dashboard code (not per tenant — tenantId is in the payload for filtering). This keeps subscription management simple: a node subscribed to a specific dashboard code processes the message and checks tenantId.

### 9.2 Version bump flow — `metadata.dashboard.upsert` handler

Atomic sequence (must all succeed or all roll back):
1. `BEGIN` PostgreSQL transaction
2. `UPDATE report_definitions SET version = version + 1, definition = $newDef WHERE code = $code AND tenant_id = $tenantId RETURNING version`
3. `COMMIT`
4. Publish `cache-invalidate:dashboard:{code}` → `{ code, version, tenantId }` to Redis pub/sub
5. (Non-transactional, best-effort) Delete `widget:{tenantId}:{code}:v*` pattern keys from Redis L1 cache

Step 4 is outside the Postgres transaction. If the publish fails, the L1 cache will expire naturally (TTL). L0 (in-memory) is evicted when the pub/sub message is received.

**On node restart**: the pub/sub subscription is re-established via `OperationRegistryRefreshService`-style `IHostedService`. Any cache version mismatch is detected because the resolver reads `version` from the current dashboard definition and uses it in the cache key — stale keys with old version numbers are structurally unreachable and expire on TTL.

### 9.3 Consumer side — `DashboardCacheInvalidationService`

```csharp
internal sealed class DashboardCacheInvalidationService : IHostedService
{
    // On start: subscribe to "cache-invalidate:dashboard:*" (pattern subscription)
    // On message: parse payload, evict L0 for matching (tenantId, code) entries
    //             L1 keys (Redis) are NOT explicitly deleted — they expire on TTL
    //             or are naturally unreachable due to version-stamped cache keys
}
```

Pattern subscription (`PSUBSCRIBE cache-invalidate:dashboard:*`) catches all dashboard codes with a single subscription.

---

## 10. Golden file test strategy (Q9)

### 10.1 Directory structure

```
tests/Transformers.Tests/
├── Transformers.Tests.csproj
├── GlobalUsings.cs
├── Helpers/
│   ├── JsonElementComparer.cs     ← deep structural equality, ignores key ordering
│   └── GoldenFileLoader.cs        ← reads from golden/ and fixtures/
├── fixtures/                      ← input row data (source of truth for tests)
│   ├── line_chart_input.json
│   ├── advanced_table_input.json
│   └── ... (one per transformer)
├── golden/                        ← expected transformer output
│   ├── line_chart.json
│   ├── advanced_table.json
│   └── ... (one per transformer)
└── Visualization/
    ├── LineChartTransformerTests.cs
    └── ... (one test class per transformer)
```

### 10.2 Test pattern

No snapshot library — manual JSON comparison with a helper:

```csharp
[Fact]
public async Task LineChartTransformer_MatchesGoldenFile()
{
    var rows    = GoldenFileLoader.LoadInputRows("line_chart_input.json");
    var config  = GoldenFileLoader.LoadWidgetConfig("line_chart_input.json");
    var result  = await new LineChartTransformer().TransformAsync(
                      new AdapterResult { Rows = rows }, config);
    var actual  = JsonSerializer.SerializeToElement(result, ResolverJsonContext.Default.LineChartData);
    var expected = GoldenFileLoader.LoadGolden("line_chart.json");

    Assert.True(JsonElementComparer.DeepEquals(actual, expected),
        $"Golden file mismatch.\nActual:\n{actual}\nExpected:\n{expected}");
}
```

### 10.3 Golden file regeneration

When a transformer output intentionally changes (e.g., field rename), regenerate golden files by running:

```
dotnet test tests/Transformers.Tests/ -- --env REGEN_GOLDEN=1
```

When `REGEN_GOLDEN=1` env var is set, the test harness writes actual output to `golden/` instead of asserting. Regeneration must be a deliberate developer action, not an automatic CI step.

### 10.4 Fixture design

Each `{type}_input.json` fixture contains:
```json
{
  "rows": [ { "col1": value, "col2": value, ... }, ... ],
  "widgetConfig": { ... },        // VisualConfig fields the transformer reads
  "tableParams": { ... }          // optional; only for table/filter types
}
```

Fixtures are minimal — 10-20 rows, chosen to exercise all code paths (e.g., null values, zero denominators for percent_of_total, empty partitions for rank).

---

## 11. Pagination eligibility check (Q10)

### 11.1 Rule (R4 above expanded)

`metadata.dashboard.upsert` must reject a widget with `chartType = "advanced_table"` if the referenced datasource cannot serve paginated results. The check is:

```csharp
bool SupportsPagination(DatasourceDefinition ds, DatasourceConfig cfg) =>
    ds.Type == "sql" && cfg.Mode is "query_builder" or "raw_sql";
```

`"timescale"` does not support pagination: time-series aggregations return fixed result sets bounded by the time range, not user-controlled page sizes.

`"grpc"` (external providers) — pagination support is declared by the provider's schema in `operation_registry.params_schema`. If the external operation's schema includes a `_table` property, it is considered pagination-eligible. This check is deferred to Phase 8.

### 11.2 Runtime behaviour

When a `dashboard.render` request includes `_table` params but the resolved widget does not support pagination (due to misconfiguration slipping through validation, or datasource type change post-upsert), the resolver:
1. Logs a warning: `"Widget {widgetId} received _table params but datasource mode '{mode}' does not support pagination; params ignored"`
2. Executes the adapter without `TableParams`
3. Returns the widget result WITHOUT pagination metadata
4. Does NOT fail the widget (graceful degradation)

The `AdvancedTableTransformer` detects the absence of `TotalCount` in `AdapterResult` and sets `"paginationDisabled": true` in its output, which the frontend can use to show a warning.

---

## 12. All 19 widget transformer specifications

Each transformer implements:
```csharp
public interface IWidgetTransformer
{
    string ChartType { get; }
    Task<JsonElement> TransformAsync(AdapterResult result, JsonElement visualConfig, CancellationToken ct = default);
}
```

Each transformer is responsible for serializing its own strongly-typed output record to `JsonElement` using the appropriate source-gen type info from `ResolverJsonContext`:

```csharp
// Example: inside LineChartTransformer
var data = new LineChartData { Labels = labels, Series = series };
return JsonSerializer.SerializeToElement(data, ResolverJsonContext.Default.LineChartData);
```

This keeps type safety per-transformer and eliminates the need for a central type-dispatch registry in the resolver. The resolver receives a `JsonElement` and places it directly into `WidgetEnvelope.Data`.

### 12.1 Visualization (10)

| # | ChartType | Input | Output record |
|---|---|---|---|
| 1 | `line_chart` | N rows, xColumn + yColumn(s) from visualConfig | `LineChartData { Labels: string[], Series: SeriesData[] }` |
| 2 | `bar_chart` | Same as line | `BarChartData { Labels: string[], Series: SeriesData[], Stacked: bool }` |
| 3 | `area_chart` | Same as line | `AreaChartData { Labels: string[], Series: SeriesData[], Fill: bool }` |
| 4 | `pie_chart` | labelColumn + valueColumn | `PieChartData { Labels: string[], Values: double[] }` |
| 5 | `donut_chart` | Same as pie | `DonutChartData { Labels: string[], Values: double[], HoleRatio: double }` |
| 6 | `kpi` | Single-row; valueColumn, optional deltaColumn, optional trendColumn | `KpiData { Value: string, Delta: double?, DeltaDir: string?, Trend: double[]? }` |
| 7 | `gauge` | Single value; visualConfig supplies min/max/thresholds | `GaugeData { Value: double, Min: double, Max: double, Thresholds: GaugeThreshold[] }` |
| 8 | `heatmap` | rowColumn + colColumn + valueColumn | `HeatmapData { Rows: string[], Cols: string[], Cells: double?[][] }` |
| 9 | `scatter` | xColumn + yColumn + optional labelColumn | `ScatterData { Points: ScatterPoint[] }` |
| 10 | `funnel` | labelColumn + valueColumn; ordered by row position | `FunnelData { Stages: FunnelStage[] }` |

`SeriesData = { Name: string, Data: double?[] }`. All column names are read from `visualConfig`; missing columns produce nulls, not exceptions.

### 12.2 Tables (3)

| # | ChartType | Output record |
|---|---|---|
| 11 | `simple_table` | `SimpleTableData { Columns: ColumnDef[], Rows: object?[][], TotalCount: int? }` |
| 12 | `advanced_table` | `AdvancedTableData { Columns: ColumnDef[], Rows: object?[][], Page: int, PageSize: int, TotalCount: int, SortBy: string?, SortDir: string, PaginationDisabled: bool }` |
| 13 | `pivot_table` | `PivotTableData { RowHeaders: string[], ColHeaders: string[], Cells: object?[][], RowTotals: double[]?, ColTotals: double[]? }` |

`ColumnDef = { Key: string, Label: string, Type: string, Sortable: bool, Filterable: bool }`.

`AdvancedTableTransformer` uses `AdapterResult.TotalCount` for the `TotalCount` field. If `TotalCount` is null (pagination not supported), sets `PaginationDisabled = true` and `TotalCount = Rows.Length`.

### 12.3 Filters (4)

| # | ChartType | Output record | Notes |
|---|---|---|---|
| 14 | `filter_dropdown` | `FilterDropdownData { Options: FilterOption[], Multiple: bool, DefaultValue: JsonElement? }` | If `optionsSource` in visualConfig: receives pre-fetched options via `WidgetRenderContext` (see §7.1 pre-fetch phase); otherwise uses `staticOptions` from visualConfig. Transformer itself makes no adapter calls. |
| 15 | `filter_date_range` | `FilterDateRangeData { Min: string?, Max: string?, DefaultStart: string?, DefaultEnd: string? }` | Dates are ISO 8601 strings; min/max from adapter; defaults from visualConfig |
| 16 | `filter_slider` | `FilterSliderData { Min: double, Max: double, Step: double, DefaultValue: double? }` | min/max from adapter result (single-row `SELECT MIN(), MAX()`); step from visualConfig |
| 17 | `filter_search` | `FilterSearchData { Placeholder: string, MinChars: int, Debounce: int }` | No adapter call; purely config-driven. Search endpoint wired in Phase 5. |

`FilterOption = { Label: string, Value: JsonElement }`.

`FilterDropdownTransformer` with `optionsSource`: reads pre-fetched options from `WidgetRenderContext.DropdownOptions` (keyed by `widgetId`). The transformer never calls the adapter directly — all DB I/O for options is done in the resolver's pre-fetch phase (see §7.1).

### 12.4 Layout (2)

| # | ChartType | Output record | Notes |
|---|---|---|---|
| 18 | `text_widget` | `TextWidgetData { RenderedHtml: string }` | Four-step pipeline (see below): substitute → render → sanitize → output. `{{filterKey}}` placeholders substituted from active filters only; no arbitrary template evaluation. |
| 19 | `tab_container` | `TabContainerData { Tabs: TabDef[] }` | No adapter call; reads `visualConfig["tabs"]` which is `[{label, widgetIds[]}]`. The resolver does NOT recursively render tab children at this stage — tab child widgets are declared as top-level widgets in `DashboardDefinition.Widgets` and rendered in the same fan-out pass. |

**`text_widget` rendering pipeline** (XSS-safe):
```
rawMarkdown (from VisualConfig["template"])
  │
  ├─ 1. substituePlaceholders(rawMarkdown, allowlistedFilters)
  │      Pattern: \{\{([a-zA-Z0-9_]+)\}\}
  │      Only substitutes keys present in active filter map; unknown keys pass through unchanged.
  │      Filter VALUES are HTML-encoded before substitution to prevent markup injection via filter input.
  │
  ├─ 2. Markdig.Markdown.ToHtml(substituted, pipeline)
  │      Pipeline: CommonMark + SafeUrl (blocks data: and javascript: in links)
  │
  ├─ 3. HtmlSanitizer.Sanitize(html)
  │      Strips: <script>, <style>, on* event attrs, javascript: URIs, data: URIs, <iframe>, <object>
  │      Allows: standard text/formatting tags, <a href="http(s):">, <img src="http(s):">
  │
  └─ 4. TextWidgetData { RenderedHtml = sanitizedHtml }
```

### 12.5 ComputedColumnEngine (NOT an `IWidgetTransformer`)

`Engine/ComputedColumnEngine.cs` implements `IComputedColumnEngine` — it is explicitly **not** an `IWidgetTransformer` and is never registered in `ITransformerRegistry`. It has no `ChartType` property. It is invoked by the resolver between the adapter result and the transformer call (see §6). It operates on the raw row set and returns an augmented row set; it does not produce `JsonElement` output. Total `IWidgetTransformer` implementations: **19**.

---

## 13. Test plan

### 13.1 `tests/QueryBuilder.Tests/`

| Test | What it verifies |
|---|---|
| `WhitelistValidator_RejectsUnknownSource` | `GetAsync` returns null → exception |
| `WhitelistValidator_RejectsDisallowedColumn` | column not in `AllowedColumns` → exception |
| `WhitelistValidator_AllowsWhenAllowedColumnsEmpty` | empty `AllowedColumns` = all columns allowed |
| `SqlKataQueryBuilder_BuildsPaginatedQuery` | `LIMIT 25 OFFSET 0` emitted for page 1 / size 25 |
| `TableFilter_LikeOp_Parameterized` | `LIKE $1` in output, not string concat |

### 13.2 `tests/Transformers.Tests/`

One `[Fact]` per transformer against the golden file (20 tests). Additional targeted tests:
- `ComputedColumnEngine_PercentOfTotal_SumsTo100`
- `ComputedColumnEngine_RunningTotal_Monotonic`
- `ComputedColumnEngine_ZScore_MeanZero`
- `ComputedColumnEngine_MomChange_NullOnFirstRow`
- `ComputedColumnEngine_MomChange_RejectsNonTimeOrderedConfig` — `DatasourceConfig.DefaultSortDir = "desc"` with `mom_change` transform raises `MISSING_TIME_ORDER_FOR_TIME_TRANSFORM` at validation (R8); the engine itself asserts temporal-ascending invariant at runtime as a guard
- `AdvancedTableTransformer_PaginationDisabled_WhenTotalCountNull`
- `FilterDropdownTransformer_StaticOptions_NoAdapterCall`
- `TextWidgetTransformer_PlaceholderSubstitution_OnlyAllowlistedKeys`
- `TextWidgetTransformer_UnknownPlaceholder_PassedThrough` (not substituted, not errored)

### 13.3 `tests/Resolver.Tests/`

| Test | Category |
|---|---|
| `DashboardResolver_CacheHit_SkipsAdapter` | Unit (FakeAdapter + FakeCache) |
| `DashboardResolver_WidgetFailure_OtherWidgetsComplete` | Unit (one adapter throws, rest succeed) |
| `DashboardResolver_SemaphoreEnforced_MaxConcurrency` | Unit (stopwatch measurement) |
| `WidgetDefinitionValidator_R1_UnknownChartType_Fails` | Unit |
| `WidgetDefinitionValidator_R4_AdvancedTable_TimescaleSource_Fails` | Unit |
| `WidgetDefinitionValidator_R9_DuplicateWidgetId_Fails` | Unit |
| `DashboardResolver_VersionBump_InvalidatesCache` | Unit (FakeCache + FakePubSub) |
| `DashboardResolver_PostgresAdapter_RealQuery` | Integration (Testcontainers, deferred to Phase 12) |

**`DashboardResolver_VersionBump_InvalidatesCache` test logic**:
1. Render dashboard `v1` with filters `{status:"active"}` → adapter called, result cached in FakeCache under key `widget:tenant1:sales:v1:w1:{hash}`
2. Render same dashboard with same filters → assert adapter NOT called (cache hit)
3. Fire `DashboardCacheInvalidationService.OnMessage({code:"sales", version:2, tenantId:"tenant1"})` directly
4. Assert L0 entry for `(tenant1, sales)` is evicted from FakeCache
5. Render dashboard `v2` (resolver now reads v2 definition) with same filters → assert adapter called (cache miss; v2 key differs from v1 key structurally)
6. Assert v1 cache entries are no longer returned (they are version-stamped; v2 render uses a different key prefix)

This test is the contract test for §9 — it verifies that version-bump + pub/sub message + L0 eviction form a correct invalidation chain.

---

## 14. Packages summary

### `Shared/QueryBuilder/`

| Package | Version | Rationale |
|---|---|---|
| `SqlKata` | 2.x | Query builder; generates parameterized SQL; supports PostgreSQL compiler |
| `SqlKata.Execution` | 2.x | Executes via Dapper; included for `QueryFactory.GetAsync` convenience |
| `Dapper` | 2.x | Thin ORM over Npgsql; used by SqlKata.Execution |
| `Npgsql` | 9.0.x | Already in Providers; shared version |
| `Microsoft.Extensions.Caching.Memory` | 9.0.x | L0 in-process cache for `queryable_sources` |

### `Shared/Adapters/`

| Package | Version | Rationale |
|---|---|---|
| `Npgsql` | 9.0.x | Direct queries for raw_sql + timescale adapters |

`QueryBuilder` project reference provides SqlKata access for the `SqlQueryBuilderAdapter`.

### `Shared/Transformers/`

| Package | Version | Rationale |
|---|---|---|
| `Markdig` | 0.37.0 (exact, NOT range) | Markdown → HTML for `TextWidgetTransformer`; pinned to specific patch — see DECISIONS.md §Markdown rendering policy |
| `HtmlSanitizer` | 8.x (pin exact at implementation) | XSS sanitization of Markdig HTML output; strips `<script>`, `on*` attributes, `javascript:` URIs, `data:` URIs |

No additional packages. All transformer output types are plain records serialized by source-gen STJ.

### `Shared/Resolver/`

| Package | Version | Rationale |
|---|---|---|
| `Microsoft.Extensions.Caching.Memory` | 9.0.x | L0 widget cache |
| `Microsoft.Extensions.Options` | 9.0.x | `ResolverOptions` via `IOptions<T>` |

StackExchange.Redis (L1 cache) is provided by `Shared/Caching` project reference.

### Test-only

| Package | Rationale |
|---|---|
| `xunit` | Standard test framework |
| `Testcontainers.PostgreSql` | Integration tests (deferred to Phase 12) |
| `Microsoft.NET.Test.Sdk` | Runner |
| `coverlet.collector` | Coverage |

---

## 15. What this plan does NOT cover

- External provider widgets (`"grpc"` datasource type) — Phase 10 (ExternalProviderAdapter)
- `metadata.dashboard.upsert` HTTP handler implementation — Phase 5 (Gateway)
- Report definition persistence (`report_definitions` store) — Phase 5
- Admin CRUD for `queryable_sources` — Phase 8
- SignalR push of `DashboardRenderPayload` — Phase 7
- `WidgetStale` event-driven invalidation — Phase 7
- NBomber load tests measuring `ComputedColumnEngine` p95 — Phase 12
