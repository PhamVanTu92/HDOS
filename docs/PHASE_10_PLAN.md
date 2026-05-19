# Phase 10 — External Provider Datasource Integration

**Status:** APPROVED  
**Author:** Claude (Sonnet 4.6)  
**Date:** 2026-05-19  
**Depends on:** Phase 8 (Provider Bridge + JWT), Phase 9 (Provider SDK)

---

## 1. Goal

Connect the dashboard render pipeline to External Providers. When a widget's datasource has `type: "external_provider"`, the `DashboardResolver` must route the fetch through the existing `RequestSubmissionService` → RabbitMQ → `Provider.Bridge` → gRPC stream → provider process → back through `ResponseRouter` → Redis → `ExternalProviderAdapter` — and return an `AdapterResult` indistinguishable from a SQL adapter result.

No changes to the SSE/HTTP surface, no changes to `DashboardRenderHandler`, no changes to `WidgetCacheService`. The seam is entirely inside the Adapters layer.

---

## 2. Datasource Definition Shape

Stored in Postgres `datasources.connection_config` as JSONB (same column used by SQL adapters for `mode`, `template`, etc.).

```json
{
  "type": "external_provider",
  "connectionConfig": {
    "operationName": "fraud.score",
    "providerId": "fraud-svc-v1",
    "paramMapping": {
      "transactionId": "{{filters.transaction_id}}",
      "amount":        "{{filters.amount}}"
    },
    "rowsPath":   "rows",
    "timeoutMs":  5000
  }
}
```

| Field | Required | Notes |
|-------|----------|-------|
| `operationName` | ✓ | Registered operation name in `operation_registry`. Must be `active`. |
| `providerId` | ✗ | Optional hint for routing; Bridge selects among providers supporting the operation. |
| `paramMapping` | ✓ | Maps widget filter keys to provider param names. Values are `{{filters.key}}` template tokens or literal strings. |
| `rowsPath` | ✗ | Dot-path into the provider's result payload to extract rows array. Default: root object treated as single-row array. |
| `timeoutMs` | ✗ | Per-fetch timeout. Default: 5 000 ms. Hard cap: 30 000 ms (Provider Bridge max). |

The `paramMapping` template engine is a simple `{{filters.key}}` interpolator — no turing-complete logic. Unknown tokens become JSON `null`.

---

## 2b. Patch 1 — UserId Propagation (Approved)

`UserId = request.UserId` (not `"system"`). Provider operations execute with the parent user's authorization context. `"system"` would bypass RBAC checks and risk privilege escalation through dashboard widgets.

`AdapterRequest` gains `string? UserId`. `DashboardResolver` sets it from the caller context. See §4 updated Nested Correlation table.

## 2c. Patch 2 — Coordinate Nested Timeout with Parent Deadline (Approved)

`AdapterRequest` gains `DateTimeOffset? ParentDeadline`. `DashboardResolver` sets it from the caller's `TimeoutAtUnixMs`. The adapter computes:

```csharp
var configTimeout  = TimeSpan.FromMilliseconds(Math.Min(config.TimeoutMs, 30_000));
var parentRemaining = request.ParentDeadline - DateTimeOffset.UtcNow;
var effectiveTimeout = parentRemaining < configTimeout ? parentRemaining : configTimeout;

if (effectiveTimeout <= TimeSpan.Zero)
    throw new AdapterException("PROVIDER_TIMEOUT", "Parent deadline already exceeded.");
```

`effectiveTimeout` is used for both `envelope.Options.TimeoutMs` and `WaitAsync` cancellation.

## 2d. Patch 3 — EP7 Renamed + EP11 Added (Approved)

EP7 covers the common production scenario: operation registered, no active provider, `PROVIDER_UNAVAILABLE`. EP11 covers the race where the operation is deleted mid-render (wrapped `OperationException`). Total: 11 unit tests.

---

## 3. ExternalProviderAdapter Design

### 3.1 Location

```
Shared/Adapters/Implementations/ExternalProviderAdapter.cs
Shared/Adapters/Config/ExternalProviderConfig.cs      ← deserialised connectionConfig
```

The adapter lives in the existing `Adapters` assembly alongside `SqlQueryBuilderAdapter`, `SqlRawAdapter`, and `TimescaleAdapter`. It is registered via the existing `AddAdapters()` extension.

### 3.2 DatasourceAdapterFactory Extension

`DatasourceAdapterFactory.Resolve()` currently throws `ADAPTER_NOT_SUPPORTED` for any type that is not `"sql"`. Phase 10 changes the guard:

```csharp
if (definition.Type.Equals("sql", StringComparison.OrdinalIgnoreCase))
    return ResolveSqlAdapter(definition);

if (definition.Type.Equals("external_provider", StringComparison.OrdinalIgnoreCase))
    return _externalProvider;   // singleton, injected

throw new AdapterException("ADAPTER_NOT_SUPPORTED", definition.Type);
```

`ExternalProviderAdapter` is a singleton — it holds no per-request state. All per-request state travels via method parameters.

### 3.3 FetchAsync Sequence

```
ExternalProviderAdapter.FetchAsync(request, ct)
  │
  ├─ 1. Parse ExternalProviderConfig from request.Datasource.ConnectionConfig
  ├─ 2. Build params JsonElement from paramMapping + request.Filters
  ├─ 3. Generate nested requestId (Guid.NewGuid)
  ├─ 4. Subscribe to rp:sse-terminal:{nestedRequestId}  ← BEFORE submit to avoid race
  ├─ 5. Submit RequestEnvelope via RequestSubmissionService.SubmitAsync()
  ├─ 6. Await pub/sub notification (timeout = config.TimeoutMs)
  ├─ 7. Read result from ResultStore.GetAsync(nestedRequestId)
  ├─ 8. Deserialize payload_json → AdapterResult
  └─ 9. Return AdapterResult
```

**Why subscribe before submit (step 4 before 5):** if the provider responds before the adapter subscribes, the pub/sub message is dropped. Subscribing first ensures zero-miss delivery. After the notification arrives, the adapter reads from `ResultStore` (`rp:result:{requestId}`) which has a 5-minute TTL — no race with the notification.

### 3.4 RequestEnvelope Construction

```csharp
var nestedId = Guid.NewGuid().ToString("N");
var envelope = new RequestEnvelope
{
    RequestId    = nestedId,
    TenantId     = request.TenantId,
    UserId       = request.UserId ?? "system",   // Patch 1: inherit caller's identity
    CorrelationId = request.ParentRequestId,      // parent requestId for log correlation
    Operation    = config.OperationName,
    Params       = BuildParams(config.ParamMapping, request.Filters),
    Options      = new RequestOptions
    {
        TimeoutMs = (int)effectiveTimeout.TotalMilliseconds,   // Patch 2: min(config, parentRemaining)
        Progress  = false,                                     // no SSE fan-out for nested requests
    },
};
```

`RequestSubmissionService.SubmitAsync()` does not require a `connectionId` — pass `null`. This is the existing path used by non-SSE callers.

---

## 4. Nested Correlation — Parent → Child Propagation

Each nested request carries three identifiers back to the parent:

| Field | Source | Where used |
|-------|--------|-----------|
| `CorrelationId` | parent `requestId` from `AdapterRequest.ParentRequestId` | Stored in `OperationRequestMessage`; surfaced in logs and traces |
| `UserId` | `AdapterRequest.UserId` (from parent `OperationHandlerContext.UserId`) | Provider operations execute with the requesting user's identity — not `"system"` |
| `Traceparent` | `Activity.Current?.Id` at call site in `ExternalProviderAdapter` | Propagated by `OperationRequestConsumer` → `ProviderRequestConsumer` → provider handler; creates child span |
| `TimeoutAtUnixMs` | `min(config.TimeoutMs, parentRemaining)` (Patch 2) | `ProviderRequestConsumer` checks remaining time before dispatching to gRPC |
| `Deadline` | `AdapterRequest.ParentDeadline` (set from `OperationHandlerContext.TimeoutAtUnixMs`) | Prevents nested request from outliving parent's deadline |

The parent `AdapterRequest` does not carry a `RequestId` field — the adapter generates the nested `requestId`. To propagate `CorrelationId`, `ExternalProviderAdapter` receives the parent `requestId` from the `DashboardResolver` callsite.

**Option A (chosen):** `AdapterRequest` gains an optional `ParentRequestId` field. `DashboardResolver` sets it from `OperationContext.RequestId`. `ExternalProviderAdapter` reads it to fill `CorrelationId`.

**Option B (rejected):** Use ambient `Activity.Current.TraceId` as correlation — loses the operational requestId linkage needed for support.

---

## 5. Response Wait Strategy — Pub/Sub over Polling

### Why pub/sub (not polling)

- `ResponseRouter` already publishes `rp:sse-terminal:{requestId}` after writing to `ResultStore`. No new infrastructure needed.
- Polling would add latency (poll interval) and Redis load proportional to concurrent dashboard renders.
- Pub/sub delivers within the Redis round-trip after the provider writes its terminal — typically <5 ms extra latency.

### Implementation

```csharp
// Subscribe BEFORE submit
var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
await _subscriber.SubscribeAsync(
    RedisChannel.Literal(RedisKeys.SseTerminal(nestedId)),
    (_, _) => tcs.TrySetResult(true));

await _submission.SubmitAsync(envelope, connectionId: null, ct);

using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(config.TimeoutMs));

try
{
    await tcs.Task.WaitAsync(timeoutCts.Token);
}
catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
{
    throw new AdapterException("PROVIDER_TIMEOUT",
        $"External provider '{config.OperationName}' did not respond within {config.TimeoutMs} ms.");
}
finally
{
    await _subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisKeys.SseTerminal(nestedId)));
}
```

`ISubscriber` is the StackExchange.Redis pub/sub interface, already available in the DI container from Phase 7.

---

## 6. Failure Mapping — Provider Errors → AdapterResult / AdapterException

| Scenario | Detected via | Thrown as |
|----------|-------------|-----------|
| Timeout (no terminal within `timeoutMs`) | `OperationCanceledException` from `WaitAsync` | `AdapterException("PROVIDER_TIMEOUT", ...)` |
| Parent `CancellationToken` cancelled | `OperationCanceledException` (ct) | Rethrow as-is (caller aborts render) |
| `ResultStore` returns null after notification | — | `AdapterException("PROVIDER_RESULT_MISSING", ...)` |
| `result.Status == "FAILED"` | `ResponseDispatchMessage.Status` | `AdapterException("PROVIDER_FAILED", result.ErrorCode)` |
| `result.Status == "CANCELLED"` | — | `AdapterException("PROVIDER_CANCELLED", ...)` |
| `payload_json` not deserializable to rows | `JsonException` | `AdapterException("PROVIDER_PAYLOAD_INVALID", ...)` |
| `operationName` not registered | `OperationException("OPERATION_NOT_FOUND")` from SubmitAsync | Wrap as `AdapterException("PROVIDER_OPERATION_NOT_FOUND", ...)` |
| Provider not connected (no active provider for operation) | Bridge returns `FAILED` with `NO_PROVIDER_AVAILABLE` | `AdapterException("PROVIDER_UNAVAILABLE", ...)` |

`AdapterException` is the existing exception type thrown by SQL adapters. `DashboardResolver` propagates it to `DashboardRenderHandler` which writes a `Terminal(FAILED)` message — same path as SQL errors.

---

## 7. Payload → AdapterResult Mapping

Provider operations return `payload_json` as a JSON string inside `ResponseDispatchMessage`. The mapping contract:

```json
{
  "rows": [
    { "col1": "val1", "col2": 42 },
    { "col1": "val2", "col2": 99 }
  ],
  "totalRows": 2,
  "schema": [
    { "name": "col1", "type": "string" },
    { "name": "col2", "type": "integer" }
  ]
}
```

- If `config.rowsPath` is `null`, the entire payload is treated as having a `rows` property at the root.
- If `config.rowsPath` is `"data"`, `payload["data"]` is used as the rows array.
- `totalRows` and `schema` are optional; null if absent.
- Each row is deserialized as `Dictionary<string, JsonElement>`.

This keeps the payload format a provider-side convention, documented in `docs/PROVIDER_PROTOCOL.md` §14 (new section in Phase 10).

---

## 8. Cache Integration

`WidgetCacheService` (L0 IMemoryCache + L1 Redis) is owned by `DashboardResolver`. The adapter itself does NOT cache. Cache check and store happen in the resolver — unchanged from SQL path:

```
DashboardResolver.RenderAsync()
  ├─ CheckCache(cacheKey)  ← hits before adapter is called
  ├─ [cache miss] ExternalProviderAdapter.FetchAsync()
  └─ StoreCache(cacheKey, result, datasource.CacheSeconds)
```

Cache key: `widget:{tenantId}:{dashCode}:v{version}:{widgetId}:{filtersHash}` — same key as SQL.
TTL: `datasource.CacheSeconds` from the datasource definition row — same field as SQL.

No changes to `WidgetCacheService` or `DashboardResolver` for caching. The only change to `DashboardResolver` is setting `AdapterRequest.ParentRequestId` (§4 Option A).

---

## 9. New Files

| File | Description |
|------|-------------|
| `Shared/Contracts/Operations/INestedRequestSubmitter.cs` | Interface isolating `RequestSubmissionService` from `Adapters` (breaks circular dep) |
| `Shared/Contracts/Store/IResultReader.cs` | Interface for reading `ResultStoreRecord` (allows fake in tests) |
| `Shared/Adapters/Config/ExternalProviderConfig.cs` | Deserialized `connectionConfig` for `type: external_provider` |
| `Shared/Adapters/Implementations/ExternalProviderAdapter.cs` | `IDatasourceAdapter` implementation |
| `tests/Adapters.Tests/Adapters.Tests.csproj` | New xUnit test project |
| `tests/Adapters.Tests/ExternalProviderAdapterTests.cs` | Unit tests EP1–EP11 + SI2 (skipped) |
| `tests/Adapters.Tests/Helpers/FakeSubmissionService.cs` | `INestedRequestSubmitter` stub with `OnSubmitAsync` callback |
| `tests/Adapters.Tests/Helpers/FakeRedisSubscriber.cs` | NSubstitute-backed `ISubscriber` stub with `Trigger()` method |
| `tests/Adapters.Tests/Helpers/FakeResultStore.cs` | `IResultReader` stub with seeded records |

### Modified Files

| File | Change |
|------|--------|
| `Shared/Adapters/Adapters.csproj` | Add `Caching` project ref + `StackExchange.Redis` package |
| `Shared/Adapters/Factory/DatasourceAdapterFactory.cs` | Add `external_provider` branch; inject `ExternalProviderAdapter` |
| `Shared/Adapters/Extensions/AdaptersExtensions.cs` | Register `ExternalProviderAdapter` as singleton |
| `Shared/Adapters/Models/AdapterRequest.cs` | Add `ParentRequestId?`, `UserId?`, `ParentDeadline?` (Patch 1+2) |
| `Shared/Adapters/Serialization/AdaptersJsonContext.cs` | Add `ExternalProviderConfig` to JSON context |
| `Shared/Caching/ResultStore.cs` | Implement `IResultReader` |
| `Shared/Operations/Context/OperationHandlerContext.cs` | Add `long TimeoutAtUnixMs` |
| `Shared/Operations/Dispatcher/OperationDispatcher.cs` | Set `TimeoutAtUnixMs` in `BuildContext` |
| `Shared/Operations/Dispatcher/RequestSubmissionService.cs` | Implement `INestedRequestSubmitter` |
| `Shared/Resolver/Abstractions/IDashboardResolver.cs` | Add 3 optional caller-context params |
| `Shared/Resolver/Core/DashboardResolver.cs` | Thread caller context → `AdapterRequest` in `RenderWidgetCoreAsync` |
| `Shared/Operations/Handlers/Dashboard/DashboardRenderHandler.cs` | Pass context fields to `RenderAsync` |
| `tests/Operations.Tests/Handlers/DashboardRenderHandlerTests.cs` | Update `FakeDashboardResolver` + `MakeContext` |
| `docs/PROVIDER_PROTOCOL.md` | Add §17: Provider payload contract for dashboard widgets |
| `docs/DECISIONS.md` | Add OQ-P10-A through OQ-P10-D resolutions |

---

## 10. Test Scenarios

All tests live in `tests/Adapters.Tests/` (new csproj). The test project uses `FakeSubmissionService` (captures envelope without hitting RabbitMQ) and `FakeRedisSubscriber` (allows manual trigger of pub/sub notification).

| ID | Name | What it verifies |
|----|------|-----------------|
| EP1 | `ValidConfig_SuccessPath_ReturnsAdapterResult` | Happy path: submit → notify → read result → rows mapped |
| EP2 | `ParamMapping_FiltersInterpolated_CorrectParamsJson` | `{{filters.transaction_id}}` resolves to filter value; unknown token → `null` |
| EP3 | `Timeout_NoNotification_ThrowsAdapterException_PROVIDER_TIMEOUT` | `WaitAsync` times out → `PROVIDER_TIMEOUT` |
| EP4 | `CancellationToken_Cancelled_Rethrows` | Parent `ct` cancelled → `OperationCanceledException` rethrown (not wrapped) |
| EP5 | `ResultStoreMissing_ThrowsAdapterException_PROVIDER_RESULT_MISSING` | `ResultStore.GetAsync` returns null after notification |
| EP6 | `ProviderFailed_StatusFailed_ThrowsAdapterException_PROVIDER_FAILED` | `result.Status == FAILED` with error code from provider |
| EP7 | `OperationRegistered_ButNoActiveProvider_FailedWithProviderUnavailable` | Operation exists; Bridge returns `FAILED` with `NO_PROVIDER_AVAILABLE`; adapter wraps as `AdapterException` |
| EP8 | `RowsPath_NestedExtraction_CorrectRows` | `rowsPath: "data"` → `payload["data"]` used as rows |
| EP9 | `SubscribeBeforeSubmit_NoRace_NotificationNotMissed` | Subscribe is called before `SubmitAsync`; notification fires synchronously during submit callback; result is received |
| EP10 | `CorrelationId_PropagatedFromParentRequestId` | Captured `RequestEnvelope.CorrelationId == AdapterRequest.ParentRequestId` |
| EP11 | `OperationDeletedMidRender_OperationNotFound_WrappedAsAdapterException` | Race: `SubmitAsync` throws `OperationException(OPERATION_NOT_FOUND)`; adapter wraps as `PROVIDER_OPERATION_NOT_FOUND` |
| SI2 | Integration: real RabbitMQ + Bridge (Testcontainers) | **SKIPPED** — deferred to Phase 12 |

### Test Infrastructure

```
tests/Adapters.Tests/
  ├── ExternalProviderAdapterTests.cs
  └── Helpers/
       ├── FakeSubmissionService.cs    ← captures envelopes, no bus
       ├── FakeRedisSubscriber.cs      ← ISubscriber mock; TriggerAsync(channel)
       └── FakeResultStore.cs          ← returns pre-seeded ResponseDispatchMessage
```

`FakeRedisSubscriber` implements `StackExchange.Redis.ISubscriber`. When `TriggerAsync(channel)` is called, it fires all registered callbacks for that channel synchronously on the calling thread — deterministic, no Task.Delay needed.

---

## 11. Implementation Order

1. `ExternalProviderConfig.cs` — parse `connectionConfig` JSON shape
2. `AdapterRequest.cs` — add `ParentRequestId`
3. `ExternalProviderAdapter.cs` — core logic
4. `DatasourceAdapterFactory.cs` — add `external_provider` branch
5. `AdaptersExtensions.cs` — register singleton
6. `DashboardResolver.cs` — pass `ParentRequestId`
7. `tests/Adapters.Tests/` — test project + EP1–EP10
8. `docs/PROVIDER_PROTOCOL.md` — §14 payload contract

---

## 12. Open Questions

| ID | Question | Default / Recommendation |
|----|----------|--------------------------|
| OQ-P10-A | Should `AdapterRequest.ParentRequestId` be added to the `AdapterRequest` record, or passed via a separate `AdapterContext` wrapper? | Add to `AdapterRequest` directly — it has precedent (other optional fields); an `AdapterContext` wrapper would change all call sites. |
| OQ-P10-B | What happens if the same `operationName` maps to multiple active providers? Bridge selects one per its routing table. Should `ExternalProviderAdapter` specify `providerId` in the request? | Yes — include `providerId` as an optional routing hint in `RequestEnvelope`; Bridge ignores it if null. |
| OQ-P10-C | If a widget's datasource `cacheSeconds = 0`, the resolver bypasses cache. Should `ExternalProviderAdapter` enforce a minimum timeout independent of cache? | No — timeout is separate from cache TTL; keep them independent. `timeoutMs` in `connectionConfig` governs fetch deadline only. |
| OQ-P10-D | Where should the `ExternalProviderConfig` schema be validated — at `DatasourceAdapterFactory.Resolve()` or inside `FetchAsync()`? | At `Resolve()` — fail fast at dashboard load time, not per-render. Throw `AdapterException("PROVIDER_CONFIG_INVALID", ...)`. |

---

## 13. Out of Scope (Phase 10)

- **Progress events from nested provider calls** — providers may emit gRPC `Progress` messages, but these will not be forwarded to the parent SSE stream. Deferred to Phase 11.
- **Fan-out to multiple providers per widget** — single provider per datasource in Phase 10.
- **Provider selection / load balancing** — Bridge handles routing; this phase passes a hint only.
- **Testcontainers integration test** (SI2) — deferred to Phase 12.
- **`docs/PROVIDER_PROTOCOL.md` §14** — written as part of Phase 10 step 8 above.

---

*Target: 8 new/modified files, 10 unit tests, 0 new infrastructure.*
