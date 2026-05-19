# PHASE_5_PLAN.md — Operations Handlers, Metadata Persistence & Dispatcher
> Status: APPROVED | Author: Claude Sonnet 4.6 | Date: 2026-05-19

This document covers design decisions for Phase 5. All open questions resolved; patches 1–5 applied; Issue X resolved. Code may begin.

---

## 0. Issue X Resolution — `RequestSubmissionService` signature

### 0.1 Facts from code

`RequestSubmissionService` **does not exist** in Phase 2 code. Phase 2 referenced it in documentation only (`PHASE_2_PLAN.md §6.1`). No `SubmissionContext` type exists anywhere in the codebase. Phase 5 creates both.

The real `RequestEnvelope` (Phase 2 code, `Shared/Contracts/Envelopes/RequestEnvelope.cs`):

```csharp
public sealed record RequestEnvelope
{
    public required string RequestId   { get; init; }
    public required string Operation   { get; init; }
    public required JsonElement Params { get; init; }
    public required string TenantId    { get; init; }
    public required string UserId      { get; init; }
    public string? CorrelationId       { get; init; }
    public RequestOptions Options      { get; init; } = new();
}

public sealed record RequestOptions
{
    public bool Progress        { get; init; } = false;   // WantsProgress
    public int? CacheSeconds    { get; init; }
    public Priority Priority    { get; init; } = Priority.Normal;
    public int? TimeoutMs       { get; init; }
}
```

`OperationRequestMessage` (Phase 2 contract) adds beyond `RequestEnvelope`:
`TimeoutAtUnixMs` (computed), `WantsProgress` (= `Options.Progress`),
`Traceparent` (captured from `Activity.Current`), `ConnectionId?` (NOT in envelope),
`ParentRequestId?` (null for client-submitted).

### 0.2 Canonical signature

```csharp
public sealed class RequestSubmissionService
{
    public Task<SubmitAck> SubmitAsync(
        RequestEnvelope envelope,   // contains TenantId, UserId, CorrelationId, Options.*
        string? connectionId,       // SignalR Hub.Context.ConnectionId; null for HTTP
        CancellationToken ct = default);
}
```

- `WantsProgress` ← `envelope.Options.Progress`
- `Priority` ← `envelope.Options.Priority` (routing key)
- `TimeoutMs` ← `envelope.Options.TimeoutMs ?? registryEntry.TimeoutMs`
- `CacheSeconds` ← `envelope.Options.CacheSeconds`
- `Traceparent` ← `Activity.Current?.Id ?? ""` (captured inside method)
- `connectionId` ← caller supplies from Hub/HTTP context

The "9-parameter §11 variant" from the draft plan is **eliminated**. The "2-param + context" Phase 2 doc description collapses to this 2-param form — `SubmissionContext` was never code, just a doc label.

---

## 1. Project structure

### 1.1 Build order

```
Contracts   Providers   Adapters   Transformers   Resolver   Caching
    ↓            ↓           ↓            ↓             ↓         ↓
         Shared/Metadata/   ←  new CRUD project (dashboard + datasource + schema defs)
                   ↓
         Shared/Operations/ ←  new dispatcher + handler project
                   ↓
         tests/Operations.Tests/
```

`Metadata` must come before `Operations` because metadata write handlers depend on `IMetadataRepository` types.

---

### 1.2 `Shared/Metadata/` (new project)

```
Shared/Metadata/
├── Metadata.csproj
├── GlobalUsings.cs
│
├── Abstractions/
│   ├── IDashboardMetadataRepository.cs    ← CRUD for dashboard_definitions
│   ├── IDatasourceMetadataRepository.cs   ← CRUD for datasource_definitions
│   └── ISchemaMetadataRepository.cs       ← CRUD for schema_definitions (V006)
│
├── Repositories/
│   ├── PostgresDashboardMetadataRepository.cs
│   ├── PostgresDatasourceMetadataRepository.cs
│   └── PostgresSchemaMetadataRepository.cs
│
├── Results/
│   ├── UpsertResult.cs            ← { Id, Version }
│   └── DeleteResult.cs            ← { Deleted: bool }
│
└── Extensions/
    └── MetadataExtensions.cs      ← AddPlatformMetadata(IServiceCollection)
```

**Dependencies**: Contracts, Npgsql 9.0.3, Dapper 2.1.35, StackExchange.Redis (for version-bump pub/sub), DI/Logging abstractions.

---

### 1.3 `Shared/Operations/` (new project)

```
Shared/Operations/
├── Operations.csproj
├── GlobalUsings.cs
│
├── Abstractions/
│   └── IOperationHandler.cs
│
├── Context/
│   ├── OperationHandlerContext.cs
│   ├── ProgressUpdate.cs
│   └── Params/
│       ├── DashboardListParams.cs
│       ├── DashboardGetParams.cs
│       ├── DashboardRenderParams.cs
│       ├── WidgetRenderParams.cs
│       ├── WidgetFilterOptionsParams.cs
│       ├── WidgetTableExportParams.cs
│       ├── WidgetDrillContextParams.cs
│       ├── DatasourceListParams.cs
│       ├── DatasourceGetParams.cs
│       ├── DatasourcePreviewParams.cs
│       ├── MetadataDashboardUpsertParams.cs
│       ├── MetadataDashboardDeleteParams.cs
│       ├── MetadataDatasourceUpsertParams.cs
│       ├── MetadataDatasourceDeleteParams.cs
│       ├── MetadataSchemaUpsertParams.cs
│       ├── AdminProvidersListParams.cs
│       ├── AdminProvidersReloadParams.cs
│       └── AdminCacheFlushParams.cs
│
├── Dispatcher/
│   ├── OperationHandlerRegistry.cs     ← IReadOnlyDictionary<string, IOperationHandler>
│   ├── OperationDispatcher.cs          ← resolve + validate + call + wrap response
│   └── RequestSubmissionService.cs     ← (RequestEnvelope, connectionId?) → SubmitAck
│
├── Progress/
│   └── ProgressReporter.cs             ← Channel<ProgressUpdate> → ProgressRingBuffer bridge
│
├── Services/
│   └── FilterOptionsService.cs         ← extracted from Phase 4 DashboardResolver pre-fetch
│
├── Handlers/
│   ├── Dashboard/
│   │   ├── DashboardListHandler.cs         (dashboard.list)
│   │   ├── DashboardGetHandler.cs          (dashboard.get)
│   │   └── DashboardRenderHandler.cs       (dashboard.render)
│   ├── Widget/
│   │   ├── WidgetRenderHandler.cs          (widget.render)
│   │   ├── WidgetFilterOptionsHandler.cs   (widget.filterOptions)
│   │   ├── WidgetTableExportHandler.cs     (widget.tableExport)
│   │   └── WidgetDrillContextHandler.cs    (widget.drillContext)
│   ├── Datasource/
│   │   ├── DatasourceListHandler.cs        (datasource.list)
│   │   ├── DatasourceGetHandler.cs         (datasource.get)
│   │   └── DatasourcePreviewHandler.cs     (datasource.preview)
│   ├── Metadata/
│   │   ├── MetadataDashboardUpsertHandler.cs    (metadata.dashboards.upsert)
│   │   ├── MetadataDashboardDeleteHandler.cs    (metadata.dashboards.delete)
│   │   ├── MetadataDatasourceUpsertHandler.cs   (metadata.datasources.upsert)
│   │   ├── MetadataDatasourceDeleteHandler.cs   (metadata.datasources.delete)
│   │   └── MetadataSchemaUpsertHandler.cs       (metadata.schemas.upsert)
│   └── Admin/
│       ├── AdminProvidersListHandler.cs          (admin.providers.list)
│       ├── AdminProvidersReloadHandler.cs        (admin.providers.reload)
│       └── AdminCacheFlushHandler.cs             (admin.cache.flush)
│
└── Extensions/
    └── OperationsExtensions.cs    ← AddPlatformOperations(IServiceCollection)
```

**Dependencies**: Contracts, Metadata, Providers, Adapters, Transformers, Resolver, Caching, Messaging, DI/Logging/Options abstractions.

---

## 2. `IOperationHandler` interface

```csharp
/// <summary>
/// Contract for all operation handlers. Each implementation is singleton-safe
/// (stateless); all per-request state flows through <see cref="OperationHandlerContext"/>.
/// </summary>
public interface IOperationHandler
{
    /// <summary>
    /// Dot-notation operation name, e.g. <c>"dashboard.render"</c>.
    /// Must match the operation name registered in <c>operation_registry</c>.
    /// </summary>
    string OperationName { get; }

    /// <summary>
    /// Execute the operation. Returns the serialized result as <see cref="JsonElement"/>.
    /// Throw <see cref="OperationException"/> for domain errors (code + message).
    /// Any other exception propagates as <c>INTERNAL_ERROR</c>.
    /// </summary>
    Task<JsonElement> HandleAsync(
        OperationHandlerContext context,
        CancellationToken ct = default);
}
```

### 2.1 `OperationHandlerContext`

```csharp
public sealed record OperationHandlerContext
{
    public required string      RequestId  { get; init; }
    public required string      TenantId   { get; init; }
    public required string      UserId     { get; init; }
    public required JsonElement Params     { get; init; }  // pre-validated params

    /// <summary>
    /// Non-null when <see cref="RequestOptions.Progress"/> was true.
    /// Handlers report progress via <c>Report((percent, message))</c>.
    /// Fire-and-forget — do not await.
    /// </summary>
    public IProgress<ProgressUpdate>? Progress { get; init; }

    /// <summary>W3C traceparent from originating request. Restore for child Activity spans.</summary>
    public required string Traceparent { get; init; }
}

public readonly record struct ProgressUpdate(int Percent, string Message);
```

### 2.2 Error contract — `OperationException`

**`OperationException` does not exist in Phase 2 code.** Phase 5 creates it in `Shared/Contracts/Exceptions/`:

```csharp
namespace ReportingPlatform.Contracts.Exceptions;

public sealed class OperationException : Exception
{
    public string Code { get; }

    public OperationException(string code, string message)
        : base(message) => Code = code;

    public OperationException(string code, string message, Exception inner)
        : base(message, inner) => Code = code;
}
```

**Error dispatch table** (all caught by `OperationDispatcher.DispatchAsync`):

| Exception type | `Status` | `Error.Code` |
|---|---|---|
| `OperationException` | `Failed` | `ex.Code` |
| Params validation failure | `ValidationError` | field-level `ValidationError[]` |
| `OperationCanceledException` | `Timeout` | `OPERATION_TIMEOUT` |
| Deadline expired before dispatch | `Timeout` | `DEADLINE_EXCEEDED` |
| `OperationHandlerRegistry` miss | `Failed` | `HANDLER_NOT_FOUND` |
| Any other `Exception` | `Failed` | `INTERNAL_ERROR` (message logged, not exposed) |

### 2.3 Telemetry conventions

- `Shared/Telemetry/ActivitySources.cs` gains `public static readonly ActivitySource Operations = new("ReportingPlatform.Operations")`.
- Dispatcher starts `ActivitySources.Operations.StartActivity("operation.dispatch")` tagged with `operation.name`, `tenant.id`, `request.id`.
- Handlers restore parent context from `context.Traceparent` via `ActivityContext.TryParse` and start child activities.
- All telemetry is fire-and-forget; exceptions in `Activity` instrumentation are swallowed.

### 2.4 `ProgressReporter` implementation (Patch 2)

`IProgress<ProgressUpdate>.Report()` is synchronous void; `ProgressRingBuffer.AppendAsync()` is async. Bridge via a bounded `Channel<ProgressUpdate>`:

```
ProgressReporter implements IProgress<ProgressUpdate>, IAsyncDisposable

Constructor:
  - Creates Channel<ProgressUpdate>.CreateBounded(new BoundedChannelOptions(100)
      { FullMode = BoundedChannelFullMode.DropOldest })
  - Starts background Task: ReadAllAsync → ProgressRingBuffer.AppendAsync (in order)

Report(ProgressUpdate update):
  - TryWrite to channel (non-blocking; drops oldest if full — bounded capacity enforced)

DisposeAsync():
  - Completes the channel writer
  - Awaits the drain task to flush remaining events to Redis before handler response is sent
```

Location: `Shared/Operations/Progress/ProgressReporter.cs`

`OperationDispatcher` creates one `ProgressReporter` per request when `WantsProgress=true`; disposes it (via `await using`) after `HandleAsync` returns, ensuring all progress events are flushed before the response message is published.

---

## 3. Operation dispatcher mechanism

### 3.1 `OperationHandlerRegistry`

```csharp
internal sealed class OperationHandlerRegistry
{
    // Key: OperationName, OrdinalIgnoreCase
    private readonly IReadOnlyDictionary<string, IOperationHandler> _map;

    public OperationHandlerRegistry(IEnumerable<IOperationHandler> handlers)
    { /* throws InvalidOperationException on duplicate */ }

    public IOperationHandler? Resolve(string operationName) =>
        _map.TryGetValue(operationName, out var h) ? h : null;

    public IReadOnlyCollection<string> RegisteredOperations => _map.Keys.ToList();
}
```

### 3.2 `OperationDispatcher.DispatchAsync` flow

Called by the `Operation.Router.Worker` MassTransit consumer (separate worker service, not in `Shared/Operations/`):

```
OperationRequestMessage arrives
        │
        ▼
1. Deadline check: if TimeoutAtUnixMs < UtcNow.ToUnixTimeMilliseconds()
   → publish OperationResponseMessage(Status=Timeout, Code=DEADLINE_EXCEEDED) and return
        │
        ▼
2. OperationHandlerRegistry.Resolve(message.Operation)
   → null: respond HANDLER_NOT_FOUND
        │
        ▼
3. IParamsValidator.ValidateAsync(message.Operation, paramsJson, ct)
   → invalid: respond VALIDATION_ERROR with field errors
        │
        ▼
4. Build OperationHandlerContext
   Create deadline-linked CancellationTokenSource(TimeoutAtUnixMs - UtcNow)
   If WantsProgress: create ProgressReporter(ProgressRingBuffer, requestId, tenantId)
        │
        ▼
5. await using (progressReporter)  ← ensures drain before response
   {
       result = await handler.HandleAsync(context, cts.Token)
   }
        │
        ▼
6. Serialize result → PayloadJson
   Publish OperationResponseMessage(Status=Success, PayloadJson)

Catch OperationException       → respond Failed(ex.Code, ex.Message)
Catch OperationCanceledException → respond Timeout(OPERATION_TIMEOUT)
Catch Exception                → log + respond Failed(INTERNAL_ERROR)
```

### 3.3 `RequestSubmissionService`

**Canonical signature** (Issue X resolved — see §0):

```csharp
public sealed class RequestSubmissionService
{
    public Task<SubmitAck> SubmitAsync(
        RequestEnvelope envelope,   // all fields including Options.*
        string? connectionId,       // SignalR Hub.Context.ConnectionId; null for HTTP
        CancellationToken ct = default);
}
```

Internal steps:

```
1. IOperationRegistry.ResolveAsync(envelope.Operation)
   → null or Status != "active": throw OperationException("OPERATION_NOT_FOUND"/"OPERATION_NOT_ACTIVE")

2. RBAC: if registryEntry.RequiredRole not null
   → resolve user roles (Phase 6 wires JWT claim extraction via IUserRoleChecker stub)
   → throw OperationException("FORBIDDEN") if role absent

3. Layer 1 envelope validation:
   - params JSON size ≤ 65 536 bytes
   - requestId non-empty
   → throw OperationException("VALIDATION_ERROR") if fails

4. Layer 2 params validation: IParamsValidator.ValidateAsync(operation, paramsJson)
   → throw OperationException("VALIDATION_ERROR") with field errors if fails

5. Compute TimeoutAtUnixMs:
   effectiveTimeout = Min(envelope.Options.TimeoutMs ?? registryEntry.TimeoutMs, MAX_TIMEOUT_MS)
   TimeoutAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + effectiveTimeout

6. IdempotencyStore.TryClaimAsync(tenantId, envelope.RequestId, ttl=effectiveTimeout*2ms)
   → false: return existing SubmitAck (idempotent re-submission, deadline NOT extended)

7. Build OperationRequestMessage:
   - All RequestEnvelope fields
   - WantsProgress = envelope.Options.Progress
   - ConnectionId  = connectionId
   - TimeoutAtUnixMs (computed above)
   - Traceparent = Activity.Current?.Id ?? ""
   - ParentRequestId = null (client-submitted)

8. Publish to priority queue via IBus:
   routing key = envelope.Options.Priority switch { High → *.high, Low → *.low, _ → *.normal }

9. Return SubmitAck { RequestId, QueuedAt=UtcNow.ToString("O"), ProgressStreamUrl? }
```

### 3.3.1 Idempotency + deadline interaction (Patch 3)

Re-submitting the **same `requestId`** (idempotency key) does NOT extend the deadline of the in-flight request. The second caller receives the same `SubmitAck` immediately. Guidance for clients:

- Use **new `requestId`** if the intent is a retry with a fresh deadline.
- Reuse `requestId` only when checking for a result from a previous submission (e.g. reconnect scenario).
- This behaviour is documented in `PROTOCOL.md §retry-handling` (to be added in Phase 6 when the protocol section is extended).

---

## 4. Per-handler params validation flow

The dispatcher runs `IParamsValidator` at step 4 above before constructing the context. Individual handlers receive pre-validated `context.Params` and deserialize into typed records using source-gen JSON contexts.

Each handler's typed params record lives in `Shared/Operations/Context/Params/`. Shape-mismatch after schema validation (structurally impossible if schema is correct) → handler throws `OperationException("INVALID_PARAMS", ...)`.

Typed params summary:

| Handler | Params record | Key fields |
|---|---|---|
| `DashboardListHandler` | `DashboardListParams` | `tenantId?` |
| `DashboardGetHandler` | `DashboardGetParams` | `dashboardCode` |
| `DashboardRenderHandler` | `DashboardRenderParams` | `dashboardCode`, `filters?`, `tableParams?` |
| `WidgetRenderHandler` | `WidgetRenderParams` | `dashboardCode`, `widgetId`, `filters`, `tableParams?` |
| `WidgetFilterOptionsHandler` | `WidgetFilterOptionsParams` | `dashboardCode`, `widgetId`, `search?` |
| `WidgetTableExportHandler` | `WidgetTableExportParams` | `dashboardCode`, `widgetId`, `filters`, `format` |
| `WidgetDrillContextHandler` | `WidgetDrillContextParams` | `sourceDashboard`, `widgetId`, `clickedData`, `targetDashboard`, `currentFilters?` |
| `DatasourceListHandler` | `DatasourceListParams` | `tenantId?` |
| `DatasourceGetHandler` | `DatasourceGetParams` | `datasourceId` |
| `DatasourcePreviewHandler` | `DatasourcePreviewParams` | `datasourceId`, `limit?` |
| `MetadataDashboardUpsertHandler` | `MetadataDashboardUpsertParams` | `definition: DashboardDefinition` |
| `MetadataDashboardDeleteHandler` | `MetadataDashboardDeleteParams` | `dashboardCode` |
| `MetadataDatasourceUpsertHandler` | `MetadataDatasourceUpsertParams` | `definition: DatasourceDefinition` |
| `MetadataDatasourceDeleteHandler` | `MetadataDatasourceDeleteParams` | `datasourceId` |
| `MetadataSchemaUpsertHandler` | `MetadataSchemaUpsertParams` | `definition: SchemaDefinition` |
| `AdminProvidersListHandler` | `AdminProvidersListParams` | _(empty)_ |
| `AdminProvidersReloadHandler` | `AdminProvidersReloadParams` | _(empty)_ |
| `AdminCacheFlushHandler` | `AdminCacheFlushParams` | `dashboardCode?` (null = flush all for tenant) |

---

## 5. Report/dashboard definition persistence — Migration V006

### 5.1 Migration status

| Table | Migration | Status |
|---|---|---|
| `dashboard_definitions` | V005 | ✅ Exists (`version INT DEFAULT 1`) |
| `datasource_definitions` | V005 | ✅ Exists |
| `schema_definitions` | **V006** | 🔲 New (needed for `metadata.schemas.upsert`) |

### 5.2 V006 DDL

```sql
-- schema_definitions: stores JSON Schema bodies referenced by operation_registry.
-- schema_id is the canonical schema identifier (e.g. "dashboard.render.params/v1").
CREATE TABLE IF NOT EXISTS schema_definitions (
    id           BIGSERIAL    PRIMARY KEY,
    tenant_id    TEXT         NOT NULL,
    schema_id    TEXT         NOT NULL,
    schema_type  TEXT         NOT NULL CHECK (schema_type IN ('params', 'payload', 'render')),
    version      TEXT         NOT NULL DEFAULT '1.0',
    schema_body  JSONB        NOT NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT uq_schema_definitions_tenant_id UNIQUE (tenant_id, schema_id)
);

CREATE INDEX IF NOT EXISTS idx_schema_definitions_tenant
    ON schema_definitions (tenant_id);

CREATE TRIGGER trg_schema_definitions_updated_at
    BEFORE UPDATE ON schema_definitions
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();  -- defined in V001
```

### 5.3 Atomic version increment SQL (upsert)

```sql
INSERT INTO dashboard_definitions
    (tenant_id, dashboard_code, title, definition, version)
VALUES
    (@TenantId, @DashboardCode, @Title, @Definition::jsonb, 1)
ON CONFLICT (tenant_id, dashboard_code)
DO UPDATE SET
    definition = EXCLUDED.definition,
    version    = dashboard_definitions.version + 1,
    updated_at = now()
RETURNING version, id;
```

Single-statement, no optimistic concurrency loop. Postgres serializes the increment within the transaction. The same pattern applies to `datasource_definitions` and `schema_definitions`.

### 5.4 Post-upsert cache invalidation

After a dashboard upsert, `PostgresDashboardMetadataRepository` publishes to Redis:
```
PUBLISH cache-invalidate:dashboard:{dashboardCode} "{tenantId}"
```
This is non-fatal — Redis failure is caught and logged. `DashboardCacheInvalidationService` (Phase 4) hears the event; L0 entries expire structurally via version-stamp mismatch.

---

## 6. Datasource definition persistence

Separate table (`datasource_definitions`, V005). Separate interface (`IDatasourceMetadataRepository`). Rationale:

- Dashboard upsert invalidates widget cache (version bump + Redis pub/sub). Datasource upsert does **not** — datasource definitions are read fresh via `PostgresDashboardDefinitionRepository.GetDatasourcesAsync` on each render; the existing 60s in-process TTL expires naturally.
- Access patterns differ: datasources are read per-widget; dashboards are read per-request.
- Narrow interfaces are easier to mock in tests.

`metadata.datasources.delete` is in scope (Q2 confirmed). It uses `DELETE FROM datasource_definitions WHERE tenant_id = @TenantId AND datasource_id = @DatasourceId RETURNING id`.

---

## 7. Drill-down context resolution algorithm

### 7.1 Input params (`WidgetDrillContextParams`)

```json
{
  "sourceDashboard": "sales_2025",
  "widgetId": "revenue_chart",
  "clickedData": { "x": "2025-06", "y": 1650000, "region": "north" },
  "targetDashboard": "sales_detail",
  "currentFilters": { "year": 2025 }
}
```

`currentFilters` is optional (supports `{{filters.*}}` tokens when present).

### 7.2 Resolution algorithm

```
1. Load source dashboard def via IDashboardDefinitionRepository.GetAsync(tenantId, sourceDashboard)
   → not found: OperationException("DASHBOARD_NOT_FOUND")

2. Find widget: dashboard.Widgets.FirstOrDefault(w => w.WidgetId == widgetId)
   → not found: OperationException("WIDGET_NOT_FOUND")

3. Read widget.InteractionConfig (JsonElement?)
   → ValueKind != Object OR no "onClickDataPoint" key: return { valid: true, resolvedFilters: {} }

4. Read interactions.onClickDataPoint.filterMapping (JsonElement)
   → not an object: return { valid: true, resolvedFilters: {} }

5. Read interactions.onClickDataPoint.targetDashboardCode
   → not equal to params.targetDashboard (case-sensitive): return { valid: false }

6. For each (filterKey, templateString) in filterMapping:
   resolved[filterKey] = Resolve(templateString, clickedData, currentFilters, context.TenantId)

7. Return DrillContextResult {
     ResolvedFilters = resolved,
     TargetDashboardCode = targetDashboard,
     Valid = true
   }
```

### 7.3 Token resolution rules

| Pattern | Resolution | Fallback |
|---|---|---|
| `{{clicked.<field>}}` | `clickedData[field]` (exact key, case-sensitive) | empty string |
| `{{filters.<key>}}` | `currentFilters[key]` (if params supplied) | empty string |
| `{{user.tenantId}}` | `context.TenantId` as JSON string | _(always resolves)_ |
| Literal (no `{{ }}`) | Pass through as JSON string | _(always)_ |
| Unknown scope `{{X.Y}}` | Preserved as literal string (non-fatal) | _(always)_ |

All resolved values are emitted as `JsonElement` (strings for text tokens; original type preserved for `{{clicked.*}}` when the clicked field is numeric/boolean).

---

## 8. Table export flow

### 8.1 Small vs large threshold

| Condition | Path | Response field |
|---|---|---|
| rowCount ≤ **5 000** | Inline sync | `ContentBase64` (non-null), `DownloadUrl` (null) |
| rowCount > 5 000 | Async stub (Phase 5) | `OperationException("LARGE_EXPORT_NOT_SUPPORTED")` |
| format = `"xlsx"` AND rowCount > 5 000 | Always async stub | same |

Threshold 5 000 keeps inline payload ≤ ~5 MB (CSV ≈ 1 KB/row avg).

### 8.2 Sync path

```
1. FetchAsync(adapter, request, ct) — no pagination limit (full dataset)
2. if rowCount > 5000: throw OperationException("LARGE_EXPORT_NOT_SUPPORTED", ...)
3. Serialize:
   - "csv":  CsvHelper 33.x → MemoryStream
   - "xlsx": ClosedXML 0.102.x → MemoryStream
4. Convert.ToBase64String(stream.ToArray())
5. Return TableExportResult {
     Format        = format,
     ContentBase64 = base64,
     FileName      = $"{widgetId}_{DateTime.UtcNow:yyyyMMdd}.{format}",
     SizeBytes     = stream.Length,
   }
```

### 8.3 Format support

| Format | Library | Package |
|---|---|---|
| CSV | CsvHelper | `CsvHelper` 33.x |
| XLSX | ClosedXML | `ClosedXML` 0.102.x |

Both produce deterministic output and are reflection-free at the serialization layer.

### 8.4 Unknown format

`format` values other than `"csv"` or `"xlsx"` → `OperationException("INVALID_PARAMS", "Unsupported format. Allowed: csv, xlsx")`.

### 8.5 Timeout configuration (Patch 4)

`widget.tableExport` is registered in `operation_registry` with `timeout_ms = 60_000` (60 seconds). This covers the sync path for up to 5 000 rows with CSV/XLSX serialization. Phase 11 will add a progress-watchdog for the async path (heartbeat every 5 s to keep the deadline token alive during object storage upload).

### 8.6 Large export deferred to Phase 11 (Q4 confirmed)

The async large-export path requires object storage. Phase 5 ships the stub. Phase 11 will:
- Select provider: **MinIO** locally (S3-compatible), **AWS S3** in production.
- Decision captured in `DECISIONS.md` (see §Q4 entry added at end of this document).

---

## 9. Test scenarios

All tests in `tests/Operations.Tests/`. All projects build `--no-incremental -warnaserror`.

### 9.1 `OperationDispatcher`

| Test | Scenario |
|---|---|
| `Dispatch_UnknownOperation_ReturnsHandlerNotFound` | Handler not in registry → HANDLER_NOT_FOUND |
| `Dispatch_InvalidParams_ReturnsValidationError` | `IParamsValidator` returns errors → VALIDATION_ERROR |
| `Dispatch_OperationException_ReturnsFailed` | Handler throws `OperationException("CUSTOM_CODE")` → FAILED |
| `Dispatch_DeadlineExceeded_ReturnsTimeout` | `TimeoutAtUnixMs` in the past → immediate DEADLINE_EXCEEDED |
| `Dispatch_CancellationDuringHandler_ReturnsTimeout` | Token cancelled mid-handler → OPERATION_TIMEOUT |
| `Dispatch_Success_ReturnsSuccessWithPayload` | Happy path → SUCCESS + PayloadJson non-null |
| `Dispatch_WantsProgress_DrainBeforeResponse` | Handler reports 2 progress events → `ProgressRingBuffer` receives both in order before response published |

### 9.2 `RequestSubmissionService`

| Test | Scenario |
|---|---|
| `Submit_UnknownOperation_Throws` | Registry returns null → OperationException |
| `Submit_InactiveOperation_Throws` | Status = "inactive" → OPERATION_NOT_ACTIVE |
| `Submit_ParamsTooLarge_Throws` | params > 65 536 bytes → VALIDATION_ERROR/PARAMS_TOO_LARGE |
| `Submit_Valid_PublishesToPriorityQueue` | Happy path → MassTransit bus publish called with correct routing key |
| `Submit_SameRequestId_SecondCall_ReturnsImmediately` | Idempotency claim taken → second call returns same SubmitAck without re-publishing |

### 9.3 `DashboardRenderHandler`

| Test | Scenario |
|---|---|
| `Render_ValidParams_ReturnsDashboardRenderPayload` | Fake IDashboardResolver → payload JSON verified |
| `Render_MissingDashboardCode_Throws` | dashboardCode absent in params → INVALID_PARAMS |

### 9.4 `WidgetDrillContextHandler`

| Test | Scenario |
|---|---|
| `DrillContext_ClickedToken_Resolved` | `{{clicked.x}}` → clickedData["x"] value |
| `DrillContext_FiltersToken_Resolved` | `{{filters.year}}` → currentFilters["year"] value |
| `DrillContext_UserTenantIdToken_Resolved` | `{{user.tenantId}}` → context.TenantId |
| `DrillContext_LiteralValue_PassedThrough` | `"north"` (no template) → JSON string "north" |
| `DrillContext_UnknownWidget_Throws` | widgetId not in dashboard → WIDGET_NOT_FOUND |
| `DrillContext_TargetMismatch_ReturnsInvalid` | targetDashboard ≠ filterMapping target → valid=false |
| `DrillContext_NoFilterMapping_ReturnsEmptyValid` | Widget has no interactionConfig → `{valid: true, resolvedFilters: {}}` |
| `DrillContext_MissingClickedField_ResolvesEmpty` | `{{clicked.unknown}}` → empty string (non-fatal) |

### 9.5 `WidgetTableExportHandler`

| Test | Scenario |
|---|---|
| `Export_SmallCsv_ReturnsBase64` | 10 rows, format=csv → ContentBase64 non-null, valid CSV after decode |
| `Export_SmallXlsx_ReturnsBase64` | 10 rows, format=xlsx → ContentBase64 non-null, XLSX magic bytes after decode |
| `Export_LargeDataset_ThrowsNotSupported` | 5 001 rows → LARGE_EXPORT_NOT_SUPPORTED |
| `Export_UnknownFormat_ThrowsInvalidParams` | format=pdf → INVALID_PARAMS |

### 9.6 `MetadataDashboardUpsertHandler`

| Test | Scenario |
|---|---|
| `Upsert_NewDashboard_ReturnsVersion1` | First upsert → UpsertResult.Version = 1 |
| `Upsert_ExistingDashboard_IncrementsVersion` | Second upsert same code → Version = 2 |
| `Upsert_PublishesCacheInvalidation` | After upsert → fake Redis subscriber receives `cache-invalidate:dashboard:{code}` |
| `Upsert_TriggersL0Eviction_E2E` *(Patch 5)* | Real `DashboardCacheInvalidationService` + `IMemoryCache` → L0 entry set before upsert → L0 entry absent after Redis pub/sub delivery (fake Redis subscriber calls `WidgetCacheService.EvictFromL0`) |

### 9.7 `WidgetFilterOptionsHandler`

| Test | Scenario |
|---|---|
| `FilterOptions_StaticOptions_NoAdapterCall` | Widget has `staticOptions` → options returned, adapter not called |
| `FilterOptions_AdapterSource_FetchesAndReturns` | Widget has `optionsSource` → `FilterOptionsService.FetchAsync` called |
| `FilterOptions_SearchFilter_AppliedToStaticOptions` | search="a" → only options whose label/value contains "a" |

### 9.8 RBAC + dispatcher integration

| Test | Scenario |
|---|---|
| `Dispatch_RequiredRoleMissing_ReturnsForbidden` | `registryEntry.RequiredRole` set; `IUserRoleChecker` returns false → FORBIDDEN |
| `Dispatch_RequiredRolePresent_HandlerCalled` | `IUserRoleChecker` returns true → handler invoked |

---

## 10. `Shared/Metadata/` repository design

### 10.1 `IDashboardMetadataRepository`

```csharp
public interface IDashboardMetadataRepository
{
    Task<UpsertResult>  UpsertAsync(string tenantId, DashboardDefinition definition, CancellationToken ct = default);
    Task<DeleteResult>  DeleteAsync(string tenantId, string dashboardCode, CancellationToken ct = default);
    Task<DashboardDefinition?> GetAsync(string tenantId, string dashboardCode, CancellationToken ct = default);
    Task<IReadOnlyList<DashboardSummary>> ListAsync(string tenantId, CancellationToken ct = default);
}

public sealed record DashboardSummary
{
    public required string DashboardCode { get; init; }
    public required string Title         { get; init; }
    public string?         Description   { get; init; }
    public required int    Version       { get; init; }
}
```

`ListAsync` uses a lightweight SQL projection — no JSONB column:
```sql
SELECT dashboard_code, title, description, version
FROM dashboard_definitions WHERE tenant_id = @TenantId ORDER BY dashboard_code;
```

### 10.2 `IDatasourceMetadataRepository`

```csharp
public interface IDatasourceMetadataRepository
{
    Task<UpsertResult> UpsertAsync(string tenantId, DatasourceDefinition definition, CancellationToken ct = default);
    Task<DeleteResult> DeleteAsync(string tenantId, string datasourceId, CancellationToken ct = default);
    Task<DatasourceDefinition?> GetAsync(string tenantId, string datasourceId, CancellationToken ct = default);
    Task<IReadOnlyList<DatasourceSummary>> ListAsync(string tenantId, CancellationToken ct = default);
}
```

`DatasourceSummary` already exists in `Contracts/RenderPayloads/Operations/DatasourceListPayload.cs`.

### 10.3 `ISchemaMetadataRepository`

```csharp
public interface ISchemaMetadataRepository
{
    Task<UpsertResult> UpsertAsync(string tenantId, SchemaDefinition definition, CancellationToken ct = default);
    Task<SchemaDefinition?> GetAsync(string tenantId, string schemaId, CancellationToken ct = default);
    Task<IReadOnlyList<SchemaDefinition>> ListAsync(string tenantId, CancellationToken ct = default);
}
```

---

## 11. `RequestSubmissionService` summary (canonical, Issue X resolved)

**Signature** (§0 canonical):
```csharp
Task<SubmitAck> SubmitAsync(RequestEnvelope envelope, string? connectionId, CancellationToken ct = default);
```

All `Priority`, `WantsProgress`, `TimeoutMs`, `CacheSeconds`, `CorrelationId` come from `envelope.Options` and `envelope.CorrelationId`. `Traceparent` captured internally. `connectionId` is the only caller-supplied field not in the envelope.

**Location**: `Shared/Operations/Dispatcher/RequestSubmissionService.cs`

**Consumers** (Phase 6):
- `Gateway.Api` HTTP controllers → pass `connectionId = null`
- `Gateway.SignalR` hub Invoke handler → pass `connectionId = Context.ConnectionId`

---

## 12. Phase 4 dependency — `FilterOptionsService` extraction (Q6)

`WidgetFilterOptionsHandler` needs the same dropdown-fetch logic as `DashboardResolver`'s pre-fetch phase (Phase 4). Rather than duplicating, Phase 5 extracts this into a shared service:

```
Shared/Operations/Services/FilterOptionsService.cs
```

```csharp
public sealed class FilterOptionsService
{
    public Task<IReadOnlyList<FilterOption>> FetchAsync(
        string tenantId,
        WidgetDefinition widget,
        DatasourceDefinition datasource,
        IReadOnlyDictionary<string, JsonElement> filters,
        string? search,
        CancellationToken ct = default);
}
```

`DashboardResolver.FetchDropdownOptionsAsync` (currently `private`) is refactored to call `FilterOptionsService.FetchAsync`. This is a small Phase 4 touchpoint: one private method extracted to a shared service. `FilterOptionsService` is registered as a singleton in `OperationsExtensions.AddPlatformOperations`.

---

## 13. Migration summary

| Migration | Status | Contents |
|---|---|---|
| V001 | ✅ Done | `operation_registry` |
| V002 | ✅ Done | `provider_registry` |
| V003 | ✅ Done | `provider_credentials_audit` |
| V004 | ✅ Done | `queryable_sources` |
| V005 | ✅ Done | `dashboard_definitions`, `datasource_definitions` |
| **V006** | 🔲 Phase 5 | `schema_definitions` |

---

## 14. Q4 deferred decision — object storage for large table exports

**Decision**: Large table exports (> 5 000 rows) are deferred to Phase 11.

**Storage provider selection**:
- **Local / dev**: MinIO (S3-compatible; Docker container)
- **Production**: AWS S3 (or Azure Blob Storage — decision at deployment time)
- **Interface**: `IObjectStorageClient` (Phase 11 defines) with `PutObjectAsync(stream, key, contentType, ct)` and `GetPresignedUrlAsync(key, expiry, ct)`

This decision is recorded in `DECISIONS.md §Q4 — Object storage for large table exports`.

---

## 15. Phase 5 completion criteria

Phase 5 ships when all of the following are satisfied:

1. **Migration V006** applied and verified (`schema_definitions` table exists)
2. **`Shared/Metadata/`** builds clean; all three repositories with positive + negative tests pass
3. **`Shared/Operations/`** builds clean; all 18 handlers + dispatcher + `RequestSubmissionService` have passing tests per §9
4. **`tests/Operations.Tests/`**: all test scenarios from §9 (including Patch 5 E2E cache invalidation test) green
5. **`FilterOptionsService` extraction** from `DashboardResolver` complete; existing `Transformers.Tests` still 35/35
6. **`OperationException`** in `Shared/Contracts/Exceptions/`
7. **Build**: `dotnet build --no-incremental -warnaserror` clean across all projects
8. **`DECISIONS.md`** updated with Q4 object storage deferred decision
9. **Verification artifacts**: file tree, build output, test counts, 4 representative file headers
