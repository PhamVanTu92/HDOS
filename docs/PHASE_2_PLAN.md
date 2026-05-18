# PHASE_2_PLAN.md — Shared/Contracts → Telemetry → Messaging → Caching
> Status: APPROVED | Author: Claude Sonnet 4.6 | Date: 2026-05-18

This document covers the design decisions for the four Phase 2 projects before any `.cs` file is written. Read top-to-bottom; each section builds on the previous.

---

## 1. Build Order and Rationale

```
Shared/Contracts   ← no dependencies on any other Shared/* project
       ↓
Shared/Telemetry   ← depends on nothing (uses Contracts for log enrichment type hints)
       ↓
Shared/Messaging   ← depends on Contracts (queue message types)
       ↓
Shared/Caching     ← depends on Contracts (store record types, progress types)
```

**Why Contracts first**: MassTransit consumer contracts, Redis store records, and RabbitMQ queue messages all need the same DTO types (`OperationRequestMessage`, `ResultStoreRecord`, `ProgressEvent`, etc.). Defining them in Contracts once prevents circular references and allows every downstream project to reference a single source of truth.

**Why Telemetry second**: all worker projects (`Messaging`, `Caching`, and later Phases) need `ActivitySource` and log enrichers. Telemetry has zero production dependencies on other Shared projects, so it's safe to build before Messaging/Caching.

---

## 2. Shared/Contracts — Full Project Structure

### 2.1 Project file (`Shared/Contracts/Contracts.csproj`)

**Runtime dependencies** (minimal by design — Contracts must be lightweight):
- `System.Text.Json` (inbox in .NET 9, no NuGet needed)
- `MessagePack` (for `[MessagePackObject]` / `[Key]` attributes on SignalR-bound types)

**No reference to**: MassTransit, StackExchange.Redis, Grpc.*, EF Core. Those layers depend on Contracts, not vice versa.

---

### 2.2 Complete file tree

```
Shared/Contracts/
│
├── Envelopes/
│   ├── RequestEnvelope.cs
│   ├── RequestOptions.cs
│   ├── SubmitAck.cs
│   └── IngestEventEnvelope.cs
│
├── Responses/
│   ├── ResponseDispatchMessage.cs
│   ├── ResponseProgressMessage.cs
│   ├── ErrorDetail.cs
│   └── WidgetStaleMessage.cs
│
├── Enums/
│   ├── ResponseStatus.cs
│   ├── Priority.cs
│   └── ErrorCodes.cs
│
├── Definitions/
│   ├── DashboardDefinition.cs
│   ├── DatasourceDefinition.cs
│   └── SchemaDefinition.cs
│
├── RenderPayloads/
│   ├── DashboardListPayload.cs
│   ├── DashboardPayload.cs
│   ├── DashboardRenderPayload.cs
│   ├── WidgetEnvelope.cs
│   ├── WidgetMeta.cs
│   ├── WidgetError.cs
│   │
│   ├── Widgets/                       ← 16 data-shape classes
│   │   ├── TimeSeriesData.cs          (shared by line_chart / bar_chart / area_chart)
│   │   ├── PieData.cs                 (shared by pie_chart / donut_chart)
│   │   ├── KpiData.cs
│   │   ├── GaugeData.cs
│   │   ├── HeatmapData.cs
│   │   ├── ScatterData.cs
│   │   ├── AdvancedTableData.cs
│   │   ├── SimpleTableData.cs
│   │   ├── PivotTableData.cs
│   │   ├── FunnelData.cs
│   │   ├── FilterDropdownData.cs
│   │   ├── FilterDateRangeData.cs
│   │   ├── FilterSliderData.cs
│   │   ├── FilterSearchData.cs
│   │   ├── TextWidgetData.cs
│   │   └── TabContainerData.cs
│   │
│   ├── Shared/                        ← sub-types reused across ≥2 widget data classes
│   │   ├── SeriesPoint.cs             (x: JsonElement, y: double?)
│   │   ├── ChartSeries.cs             (name, data: SeriesPoint[])
│   │   ├── ChartAxes.cs
│   │   ├── AxisDefinition.cs          (type, label, format)
│   │   ├── ChartAnnotation.cs
│   │   ├── PieSlice.cs                (label, value, color?)
│   │   ├── GaugeThreshold.cs
│   │   ├── HeatmapCell.cs
│   │   ├── ScatterPoint.cs
│   │   ├── ScatterSeries.cs
│   │   ├── TableColumn.cs             (key, label, type, sortable, filterable, computed?, …)
│   │   ├── TablePagination.cs         (mode, page, pageSize, totalRows, totalPages?)
│   │   ├── TableSortSpec.cs           (key, direction)
│   │   ├── TableFilterSpec.cs         (key, op, value: JsonElement)
│   │   ├── TableFooter.cs
│   │   ├── PivotDimension.cs
│   │   ├── PivotMeasure.cs
│   │   ├── PivotCell.cs
│   │   ├── FunnelStep.cs
│   │   ├── FilterOption.cs            (value, label, count?)
│   │   ├── DateRangeValue.cs          (from, to)
│   │   ├── SliderRangeValue.cs        (from, to — numeric)
│   │   ├── TabDefinition.cs           (id, label, widgetIds, default)
│   │   ├── InteractionConfig.cs
│   │   ├── ClickAction.cs             (type, targetDashboardCode, filterMapping, openMode)
│   │   ├── DrillPathLevel.cs          (level, field, label)
│   │   ├── KpiComparison.cs
│   │   └── RefreshPolicy.cs           (mode, intervalSeconds, debounceMs)
│   │
│   └── Operations/                    ← result types for non-widget operations
│       ├── DatasourceListPayload.cs
│       ├── DatasourcePreviewPayload.cs
│       ├── FilterOptionsResult.cs
│       ├── TableExportResult.cs       (downloadUrl or base64 inline)
│       └── DrillContextResult.cs      (resolvedFilters, targetDashboardCode, valid)
│
├── TableParams/                       ← request-side table params (sent inside params.filters._table)
│   ├── TablePaginationParams.cs       (page, pageSize, sort: SortSpec[], filters: FilterSpec[])
│   ├── SortSpec.cs                    (key, direction: "asc"|"desc")
│   └── FilterSpec.cs                  (key, op, value: JsonElement)
│
├── Messaging/                         ← internal queue message contracts (MassTransit)
│   ├── OperationRequestMessage.cs
│   ├── OperationResponseMessage.cs
│   ├── OperationProgressMessage.cs
│   └── CancelRequestMessage.cs
│
├── Store/                             ← Redis record types
│   ├── OwnerStoreRecord.cs
│   ├── ResultStoreRecord.cs
│   ├── IdempotencyRecord.cs
│   └── ProgressEvent.cs
│
├── Validation/
│   ├── IParamsValidator.cs
│   ├── ValidationResult.cs
│   └── ValidationError.cs
│
└── Serialization/
    ├── ClientContractsJsonContext.cs
    ├── RenderContractsJsonContext.cs
    └── MessagingContractsJsonContext.cs
```

---

### 2.3 Complete type inventory

#### Envelopes

| Type | Fields | Maps to |
|---|---|---|
| `RequestEnvelope` | `RequestId`, `Operation`, `Params: JsonElement`, `TenantId`, `UserId`, `CorrelationId?`, `Options: RequestOptions` | PROTOCOL.md §4.1 full JSON schema |
| `RequestOptions` | `Progress: bool`, `CacheSeconds: int?`, `Priority: Priority`, `TimeoutMs: int?` | PROTOCOL.md §4.2 options fields |
| `SubmitAck` | `RequestId`, `QueuedAt: string` (ISO 8601 UTC — Option B: backend converts before serialization; no DateTimeOffset formatter needed for HTTP or MessagePack), `ProgressStreamUrl: string?` | PROTOCOL.md §6.5 + TypeScript type |
| `IngestEventEnvelope` | `EventType`, `TenantId`, `OccurredAt`, `Payload: JsonElement` | PROTOCOL.md §3.6 ingest endpoints |

#### Responses

| Type | Fields | Maps to |
|---|---|---|
| `ResponseDispatchMessage` | `RequestId`, `Status: ResponseStatus`, `Operation`, `Payload: JsonElement?`, `Error: ErrorDetail?`, `ElapsedMs: long`, `TenantId` | PROTOCOL.md §5.1; SignalR push via MessagePack |
| `ResponseProgressMessage` | `RequestId`, `Percent: int`, `Message: string`, `TsUnixMs: long` | PROTOCOL.md §5.2; SSE only (no MessagePack) |
| `ErrorDetail` | `Code: string`, `Message: string`, `DetailsJson: string?`, `Retryable: bool` | PROTOCOL.md §5.3 |
| `WidgetStaleMessage` | `Channel: string`, `Hint: WidgetStaleHint` | PROTOCOL.md §6.4; SignalR push via MessagePack |
| `WidgetStaleHint` | `Reason: string` (one of `WidgetStaleReasons` constants — see below), `UpdatedAt: string` | PROTOCOL.md §6.4 |

**`WidgetStaleReasons` constants** (static class in `Responses/`, used by `WidgetStaleHint.Reason`):

```csharp
public static class WidgetStaleReasons
{
    public const string DataUpdated       = "data_updated";
    public const string MetadataChanged   = "metadata_changed";
    public const string ManualRefresh     = "manual_refresh";
    public const string CacheInvalidated  = "cache_invalidated";
}
```

#### Enums and constants

| Type | Values | Maps to |
|---|---|---|
| `ResponseStatus` | `Done`, `Failed`, `Timeout`, `Cancelled` | PROTOCOL.md §5.1 `status` field |
| `Priority` | `Low`, `Normal`, `High` | PROTOCOL.md §4.2 `options.priority` |
| `ErrorCodes` | Static `string` constants — see below | PROTOCOL.md §5.4 |

`ErrorCodes` constants (static class, not enum — codes are open-ended for external providers):
```
OPERATION_NOT_FOUND, PROVIDER_UNAVAILABLE, PROVIDER_DISCONNECTED, PROVIDER_SUSPENDED,
TIMEOUT, BACKPRESSURE, CANCELLED, VALIDATION_ERROR, DUPLICATE_REQUEST,
RATE_LIMITED, UNAUTHORIZED, FORBIDDEN, INTERNAL_ERROR
```

#### Messaging (queue contracts)

| Type | Fields | Maps to |
|---|---|---|
| `OperationRequestMessage` | All `RequestEnvelope` fields + `TimeoutAtUnixMs: long`, `WantsProgress: bool`, `Traceparent: string`, `ConnectionId: string?`, `CorrelationId: string?`, `ParentRequestId: string?` | Published by `RequestSubmissionService` to router queue. `CorrelationId` propagated from client envelope. `ParentRequestId` is null for client-submitted requests; set to the parent's `requestId` for nested calls (e.g. Resolver invoking an external provider inside `dashboard.render`) — required for Jaeger trace tree. |
| `OperationResponseMessage` | `RequestId`, `Status: ResponseStatus`, `PayloadJson: string?`, `Error: ErrorDetail?`, `ElapsedMs: long`, `TenantId`, `UserId`, `ConnectionId?`, `CorrelationId: string?` | Published by workers to dispatcher queue. `CorrelationId` propagated for end-to-end tracing. |
| `OperationProgressMessage` | `RequestId`, `Percent: int`, `Message: string`, `TsUnixMs: long`, `TenantId` | Published by Provider Bridge to progress queue |
| `CancelRequestMessage` | `RequestId`, `TenantId`, `UserId` | Published by `CancelRequest` handlers to cancellation exchange |

#### Store records

> **As-built delta (Phase 2)**: All timestamp fields on store records are `string` (ISO 8601 UTC), not `DateTimeOffset`. See DECISIONS.md §Timestamps on Redis store records.

| Type | Fields | Maps to |
|---|---|---|
| `OwnerStoreRecord` | `RequestId`, `ConnectionId: string?`, `UserId`, `TenantId`, `SubmittedAt: string` (ISO 8601 UTC) | Redis owner-store; enables SignalR routing at dispatch time. TTL=10min enforced by `OwnerStore.SetAsync`. |
| `ResultStoreRecord` | `RequestId`, `Status: ResponseStatus`, `PayloadJson: string?`, `Error: ErrorDetail?`, `ElapsedMs: long`, `TenantId`, `StoredAt: string` (ISO 8601 UTC) | Redis result-store; powers `GET /result` reconnection fallback |
| `IdempotencyRecord` | `RequestId`, `OperationKey: string`, `Status: IdempotencyStatus`, `CreatedAt: string` (ISO 8601 UTC) | Redis idempotency check; prevents double-processing. `OperationKey` is SHA-256 of `operation + canonicalParams`. |
| `ProgressEvent` | `RequestId`, `Percent: int`, `Message: string`, `Timestamp: string` (ISO 8601 UTC), `Step: string?`, `EventId: string?` | Redis Stream ring buffer (DECISIONS.md Phase 2 item); `EventId` is set on read-back from stream (XREAD entry ID), null on write. |

`IdempotencyStatus` enum: `Processing` (queued, terminal not yet known) | `Completed` (terminal stored in result-store)

#### Validation

| Type | Members | Notes |
|---|---|---|
| `IParamsValidator` | `Task<ValidationResult> ValidateAsync(string operation, JsonElement params, CancellationToken ct)` | Implemented in `Metadata.Api` / `Shared/Providers` using JsonSchema.Net; contract lives here |
| `ValidationResult` | `bool IsValid`, `IReadOnlyList<ValidationError> Errors` | — |
| `ValidationError` | `string Field`, `string Message`, `string Code` | Mapped to `ErrorDetail.DetailsJson` on failure |

#### Render payloads — widget envelope

| Type | Fields | Notes |
|---|---|---|
| `WidgetEnvelope` | `WidgetId`, `ChartType: string`, `Title`, `Subtitle?`, `VisualConfig: JsonElement`, `Interactions: InteractionConfig?`, `Data: JsonElement`, `Meta: WidgetMeta`, `IsEmpty: bool`, `Error: WidgetError?` | RENDER_CONTRACTS.md §1.2. `Data` is `JsonElement` — typed per `ChartType` at handler level. |
| `WidgetMeta` | `RenderContractVersion`, `GeneratedAt`, `FromCache: bool`, `ElapsedMs: long`, `SubscribeChannel` | RENDER_CONTRACTS.md §1.1 |
| `WidgetError` | `Code: string`, `Message: string` | RENDER_CONTRACTS.md §1.3 |

**Design note on `Data: JsonElement`**: handlers build strongly-typed data objects (e.g. `TimeSeriesData`) and serialize to `JsonElement` before placing into `WidgetEnvelope`. This avoids STJ polymorphic discriminator complexity for 16+ types, keeps Contracts simple, and gives each transformer full type safety. The 16 data classes in `Widgets/` are the strongly-typed layer.

#### Render payloads — 16 widget data classes

| C# Class | `chartType` strings | RENDER_CONTRACTS.md section |
|---|---|---|
| `TimeSeriesData` | `line_chart`, `bar_chart`, `area_chart` | §3.1 |
| `PieData` | `pie_chart`, `donut_chart` | §3.2 |
| `KpiData` | `kpi` | §3.3 |
| `GaugeData` | `gauge` | §3.4 |
| `HeatmapData` | `heatmap` | §3.5 |
| `ScatterData` | `scatter` | §3.6 |
| `AdvancedTableData` | `advanced_table` | §3.7 |
| `SimpleTableData` | `simple_table` | §3.8 |
| `PivotTableData` | `pivot_table` | §3.9 |
| `FunnelData` | `funnel` | §3.10 |
| `FilterDropdownData` | `filter_dropdown` | §4.1 |
| `FilterDateRangeData` | `filter_date_range` | §4.2 |
| `FilterSliderData` | `filter_slider` | §4.3 |
| `FilterSearchData` | `filter_search` | §4.4 |
| `TextWidgetData` | `text_widget` | §5.1 |
| `TabContainerData` | `tab_container` | §5.2 |

**`AdvancedTableData` vs `SimpleTableData` — kept separate** (not a unified `TableData` with a mode flag): they have meaningfully different pagination contracts. `AdvancedTableData` always echoes back applied sort and filters, always has `totalRows`/`totalPages`, and always has `exportHint`. `SimpleTableData` never has those fields. A single class would introduce nullable fields that are semantically required for one mode and illegal for the other — creating false optionality that confuses both C# consumers and frontend devs. The compatible-group chart-switching entry in §7 (`simple_table` ↔ `advanced_table`) is a frontend-only concern; the backend always returns the appropriate type. ✓ Agreed.

**Note**: PROTOCOL.md says "16 widget types" — confirmed: 16 distinct data shapes (3 `chartType` strings map to `TimeSeriesData`, 2 map to `PieData`). RENDER_CONTRACTS.md has 19 `chartType` discriminator strings but 16 distinct payload shapes. C# models the shapes, not the discriminator count.

---

## 3. Client-facing vs. Provider-facing DTO Boundary

### 3.1 Shapes that differ

| Concept | Client-facing (`Contracts/Envelopes/`) | Provider-facing (gRPC proto) | Difference |
|---|---|---|---|
| Request submission | `RequestEnvelope` — `Params: JsonElement`, `Options: RequestOptions`, no `timeoutAtUnixMs` | `OperationRequest` proto — `ParamsJson: string`, `TimeoutAtUnixMs: long`, `WantsProgress: bool`, no `Options` object | Client sends options; Bridge resolves timeout and flattens to unix-ms before sending to provider |
| Request identity | `requestId` (UUID v7 string, client-generated) | `requestId` (same value, propagated unchanged) | Identity is stable end-to-end |
| Auth/tenant | JWT claims (`tenantId`, `userId` from JWT) | `tenantId`, `userId` fields on `OperationRequest` (extracted from JWT by Bridge before forwarding) | Providers receive pre-extracted, validated values |
| Progress | `ResponseProgressMessage` with SignalR push | `Progress` proto chunk in `OperationResponseChunk` stream | Different serialization layer; semantically same |
| Terminal | `ResponseDispatchMessage` with `Payload: JsonElement` | `Terminal` proto with `PayloadJson: string` | String ↔ JsonElement conversion at dispatcher |
| Error | `ErrorDetail` (`code`, `message`, `detailsJson`, `retryable`) | `Error` proto (`code`, `message`, `detailsJson`) | `retryable` added by Bridge from `detailsJson.retryable` field |

### 3.2 Shapes that ARE shared (via internal queue)

`OperationRequestMessage` is the internal bridge between client-facing and provider-facing worlds. It is not exposed to either party — only to internal workers.

### 3.3 Conversion path

```
[Client]
  RequestEnvelope (JsonElement params)
      │
      ▼ RequestSubmissionService
  OperationRequestMessage (Contracts/Messaging/)
    + TimeoutAtUnixMs  ← min(options.timeoutMs, registry.maxTimeoutMs)
    + WantsProgress    ← options.progress
    + Traceparent      ← Activity.Current?.Id
    + ConnectionId     ← from X-Connection-Id header OR Hub Context.ConnectionId
      │
      ▼ RabbitMQ (router queue)
      │
      ▼ Operation.Router.Worker
      │
      ├── [Internal handler] → runs operation directly → OperationResponseMessage
      │
      └── [External handler] → forwards to Provider Bridge queue
              │
              ▼ Provider.Bridge
          OperationRequest proto (ParamsJson = params.GetRawText())
              │
              ▼ gRPC stream → Provider
              ▼ Terminal proto
          OperationResponseMessage
              │
              ▼ Response.Dispatcher.Worker
          ResponseDispatchMessage → SignalR push (MessagePack)
          ResultStoreRecord       → Redis (for GET /result fallback)
```

**Key conversion points** (to implement in Phase 8 Bridge, not Phase 2):
- `JsonElement` → `string`: `params.GetRawText()` (lossless, preserves exact JSON)
- `string` → `JsonElement`: `JsonDocument.Parse(payloadJson).RootElement` (clone to detach from document lifetime)
- Proto `Error` → `ErrorDetail`: map fields 1:1; parse `detailsJson` to extract `retryable` bool

---

## 4. JSON Serialization Strategy

### 4.1 Naming policy

**All JSON on the wire uses `camelCase`.** This matches PROTOCOL.md schemas throughout (`requestId`, `queuedAt`, `progressStreamUrl`, etc.).

Implementation: source-generated `JsonSerializerContext` with `PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase`. No per-property `[JsonPropertyName]` attributes needed for naming — only needed when C# name and JSON name genuinely differ (e.g. an acronym like `ID` vs `id`).

### 4.2 Source-generated contexts

Three contexts, each covering a logical boundary:

| Context class | Types included | Used by |
|---|---|---|
| `ClientContractsJsonContext` | `RequestEnvelope`, `RequestOptions`, `SubmitAck`, `ResponseDispatchMessage`, `ResponseProgressMessage`, `ErrorDetail`, `WidgetStaleMessage`, `IngestEventEnvelope` | `Request.Api`, `Realtime.Hub`, SSE endpoint |
| `RenderContractsJsonContext` | All 16 widget data classes, `WidgetEnvelope`, `WidgetMeta`, `DashboardRenderPayload`, `DashboardListPayload`, `DatasourceListPayload`, `DatasourcePreviewPayload`, `DrillContextResult`, `FilterOptionsResult`, `TableExportResult`, all `Shared/` sub-types | `Shared/Transformers`, `Shared/Operations` handlers |
| `MessagingContractsJsonContext` | `OperationRequestMessage`, `OperationResponseMessage`, `OperationProgressMessage`, `CancelRequestMessage`, `OwnerStoreRecord`, `ResultStoreRecord`, `IdempotencyRecord`, `ProgressEvent`, enums `Priority`, `IdempotencyStatus` | `Shared/Messaging`, `Shared/Caching` |

> **As-built delta (Phase 2)**: `ComputedTransform` is a `static class` of string constants (not an enum, not a record) and cannot be used as a type argument to `[JsonSerializable(typeof(...))]`. It is excluded from all STJ contexts. Callers reference its constants directly at compile time — no runtime serialization needed.

Context declaration pattern:
```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(RequestEnvelope))]
[JsonSerializable(typeof(SubmitAck))]
// … all types in this boundary
internal sealed partial class ClientContractsJsonContext : JsonSerializerContext { }
```

### 4.3 `params: JsonElement` consideration

`RequestEnvelope.Params` is `JsonElement`. Source-generated contexts handle `JsonElement` natively — no special treatment needed. When deserializing from HTTP body, use `JsonDocument.Parse(stream)` and extract `Params` as a cloned element before document disposal:

```csharp
// In Request.Api controller:
using var doc = JsonDocument.Parse(body);
var envelope = doc.Deserialize(ClientContractsJsonContext.Default.RequestEnvelope);
// Params is already a JsonElement; safe to use after doc disposal? No — must clone:
envelope = envelope with { Params = envelope.Params.Clone() };
```

**Binding note**: for minimal effort and correctness, use `[FromBody] RequestEnvelope envelope` with `Microsoft.AspNetCore.Http.Json.JsonOptions` configured to use `ClientContractsJsonContext`. ASP.NET Core 9 supports this with `JsonSerializerOptions.TypeInfoResolver`.

### 4.4 `JsonElement` for `Payload` in `ResponseDispatchMessage`

`Payload: JsonElement?` in `ResponseDispatchMessage` enables transparent pass-through of widget payloads without re-serialization at the dispatcher layer. The widget payload is serialized once by the transformer, stored as a JSON string in `ResultStoreRecord.PayloadJson`, then parsed back to `JsonElement` when building the push message. This avoids double-serialization overhead.

---

## 5. MessagePack Interop Strategy

### 5.1 Which types need MessagePack

Only types pushed via SignalR hub methods need MessagePack serialization. SSE uses plain STJ.

**Requires `[MessagePackObject]`**:
- `ResponseDispatchMessage`
- `ErrorDetail` (nested in `ResponseDispatchMessage`)
- `WidgetStaleMessage`
- `WidgetStaleHint`

**Does NOT require `[MessagePackObject]`** (STJ only):
- `ResponseProgressMessage` (SSE transport)
- `SubmitAck` (returned from Hub Invoke — also MessagePack, but as a return value, not a push; handled by SignalR protocol automatically)
- All queue message types
- All store records

### 5.2 Key naming strategy

Use **explicit string keys** matching the camelCase wire contract. This is intentional — it avoids relying on resolver camelCase conversion logic and makes the wire format auditable.

```csharp
[MessagePackObject]
public sealed record ResponseDispatchMessage
{
    [Key("requestId")]    public required string RequestId    { get; init; }
    [Key("status")]       public required string Status       { get; init; }  // lowercase enum string
    [Key("operation")]    public required string Operation    { get; init; }
    [Key("payload")]      public JsonElement?    Payload      { get; init; }
    [Key("error")]        public ErrorDetail?    Error        { get; init; }
    [Key("elapsedMs")]    public long            ElapsedMs    { get; init; }
    [Key("tenantId")]     public required string TenantId     { get; init; }
}
```

**Why string keys, not integer keys**: integer keys (`[Key(0)]`) are more efficient (map vs array encoding) but the JS client receives positional data that is harder to inspect in devtools. String keys are self-describing and match the PROTOCOL.md camelCase contract visible to frontend devs. Performance difference is negligible for terminal push messages (one per request).

### 5.3 `JsonElement` in MessagePack

`ResponseDispatchMessage.Payload` is `JsonElement?`. MessagePack does not natively handle `JsonElement`. **Solution**: custom MessagePack formatter for `JsonElement` that serializes it as a raw msgpack binary extension containing the JSON string, or more simply as a msgpack `str` containing the raw JSON text. The JS client receives a string it can `JSON.parse()`.

Alternative: serialize `Payload` as `string? PayloadJson` in the MessagePack model and have a separate STJ model for HTTP. This is the **two-model approach** (see §5.4 below).

### 5.4 Recommended approach: separate push DTOs

To avoid the `JsonElement`-in-MessagePack problem cleanly, use a **thin separate push DTO** for SignalR:

```
ResponseDispatchMessage         ← STJ model (HTTP GET /result, internal queues)
ResponseDispatchPushMessage     ← MessagePack model (SignalR push only)
```

`ResponseDispatchPushMessage` has `PayloadJson: string?` (the raw JSON string) instead of `JsonElement?`. The `Response.Dispatcher.Worker` builds one, converts to the other at push time. This is 5 lines of mapping code and avoids all MessagePack/JsonElement edge cases.

Both models live in `Contracts/Responses/`. The push variants are clearly named (`*PushMessage`).

**Decision needed**: approve the two-model approach or prefer a single model with a custom MessagePack formatter.

---

## 6. Validation Strategy

### 6.1 Two-layer validation

```
Layer 1 — Envelope validation (structural)
  Validates: required fields present, operation pattern matches regex,
             requestId is non-empty, tenantId matches JWT claim, params ≤ 64 KB
  Where: RequestSubmissionService (same code path for HTTP and Invoke)
  How: DataAnnotations on RequestEnvelope + explicit checks
  Error output: ValidationError[] → HubException("VALIDATION_FAILED") or HTTP 400

Layer 2 — Params validation (schema)
  Validates: params JSON conforms to registered JSON Schema for the operation
  Where: RequestSubmissionService, after envelope validation passes
  How: IParamsValidator.ValidateAsync(operation, params, ct)
  Error output: ValidationError[] (field-level errors) → same delivery as Layer 1
```

### 6.2 `IParamsValidator` location

The interface lives in `Contracts/Validation/` (just the contract + result types). The implementation (`JsonSchemaParamsValidator`) lives in a later phase project (`Shared/Providers` or `Metadata.Api`), which references `Contracts` and adds `JsonSchema.Net` as a dependency.

This keeps `Contracts` free of JSON Schema library dependencies while allowing any project to program against `IParamsValidator` without knowing the implementation.

### 6.3 Schema compilation

JSON Schemas stored in PostgreSQL (`operation_registry.params_schema` JSONB column). At startup:

1. `IOperationRegistry` loads all registered schemas from Postgres
2. For each schema, `JsonSchema.Net`'s `JsonSchema.FromText(schemaJson)` compiles it
3. Compiled schemas are cached in a `ConcurrentDictionary<string, JsonSchema>` keyed by `operationPattern`
4. On hot-reload (Redis pub/sub `operation-registry:updated`): invalidate and recompile the affected schema only

Validation per request: `schema.Evaluate(params, evaluationOptions)` — synchronous, no I/O. Typical JSON Schema validation is < 1ms for well-formed params under 64 KB.

### 6.4 `params` size guard

Before schema validation, check: `params.GetRawText().Length > 65_536` → reject with `VALIDATION_ERROR` code `PARAMS_TOO_LARGE`. This guard lives in Layer 1 (envelope validation), not Layer 2, so it applies before any schema lookup.

### 6.5 What is NOT validated in Contracts

- `tenantId` cross-tenant enforcement → Gateway (YARP) JWT validation
- `requestId` UUID v7 format → checked but not enforced to exact UUID v7; any non-empty string is accepted (idempotency key semantics only)
- operation-level RBAC (`required_role`) → checked at `RequestSubmissionService` after operation lookup, before queuing
- `params` business logic validation (e.g. date range start < end) → provider's responsibility; provider returns `VALIDATION_ERROR` terminal

---

## 7. Open Questions Requiring Decision Before Phase 2 Starts

| # | Question | Default recommendation |
|---|---|---|
| Q1 | Single `ResponseDispatchMessage` model vs. separate `ResponseDispatchPushMessage`? | **Two models** — avoids `JsonElement`-in-MessagePack friction |
| Q2 | `IdempotencyRecord` stored as JSON string in Redis or as MessagePack binary? | **JSON string** — human-readable in Redis CLI; `MessagingContractsJsonContext` handles it |
| Q3 | Should `OwnerStoreRecord.ConnectionId` be nullable (missing = no SignalR connection at submission time, fall back to user-level push)? | **Yes, nullable** — matches PROTOCOL.md §4.3 behavior |
| Q4 | Are `TablePaginationParams` / `SortSpec` / `FilterSpec` in `Contracts/TableParams/` or inside `Shared/Operations/`? | **Contracts** — used by API (deserializing `_table` from params) and by operation handlers; needs to be shareable |
| Q5 | `DashboardDefinition`, `DatasourceDefinition`, `SchemaDefinition` in Contracts or Metadata.Api only? | **Contracts** — they are request params for metadata operations, so any project accepting requests needs them |

---

## 8. Packages Summary

| Project | New NuGet packages |
|---|---|
| `Shared/Contracts` | `MessagePack` (for `[MessagePackObject]`/`[Key]` on push types; already inbox via SignalR transitive dep — explicit ref ensures version pin) |
| `Shared/Telemetry` | `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `Serilog.AspNetCore`, `Serilog.Enrichers.Environment`, `Serilog.Sinks.Console`, `Serilog.Formatting.Compact` |
| `Shared/Messaging` | `MassTransit.RabbitMQ`, `MassTransit.AspNetCore` |
| `Shared/Caching` | `StackExchange.Redis`, `MessagePack` (for optional binary Redis values) |

---

## 9. What This Plan Does NOT Cover

The following are deferred to their respective phase plans:
- `Shared/Transformers` (16 transformer classes + `ComputedColumnEngine`) — Phase 4
- `Shared/Providers` (operation registry, `JsonSchemaParamsValidator`) — Phase 3
- `Shared/QueryBuilder` (datasource query abstraction) — Phase 4
- Provider Bridge gRPC contract types — Phase 8
- `ProviderSdk` client types — Phase 9
