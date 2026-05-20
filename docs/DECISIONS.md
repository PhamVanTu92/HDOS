# DECISIONS.md — Architecture & Design Decisions
> Created: 2026-05-18 | Updated after Phase 5 (2026-05-19)

This file records non-obvious decisions made during platform design. Each entry explains the chosen behaviour, why alternatives were rejected, and what it implies for implementation. Frontend and provider teams should read entries that affect their integration surface.

---

## Multi-tab user-level fan-out

When a `connectionId`-targeted push fails (connection gone), the platform falls back to a user-level push: the response is delivered to ALL active SignalR connections for that `userId`.

**Implication**: a user with N tabs open, where one tab originated the request and that tab's WebSocket dropped, will see the response arrive on all N tabs.

**Why we accept this**:
- Simpler routing logic — no need to track tab focus or activity
- Idempotent UI render — frontend keys updates by `requestId`; tabs that didn't initiate a given `requestId` simply ignore the push (no UI element listening for it)
- Better UX in the common case — if the user moved to another tab while waiting, they still see the result there

**Frontend responsibility**:
- Track in-flight `requestId`s in each tab
- Render only updates whose `requestId` is in the local pending-set
- Ignore pushes for unknown `requestId`s (very small CPU cost; no UI flash)

**Alternative considered (rejected)**: targeted push to "focused tab only" via additional client-side metadata. Rejected because it adds complexity for marginal benefit; the duplicate-receive cost is negligible (just MessagePack decode + map lookup).

**Implementation note (Phase 7)**: `Response.Dispatcher.Worker` should push to `connectionId` first; on failure (connection not found in backplane), fall back to SignalR group `user:{userId}`.

---

## Filter change race / request supersession

**Scenario**: User changes a filter (request A in flight) → changes again (request B sent). Both responses will arrive via SignalR.

**Backend behaviour**: No automatic supersession. Both A and B execute normally. Backend has no concept of "which dashboard.render supersedes which" — they are independent requests with independent `requestId`s.

**Frontend responsibility — last-write-wins**:
1. Track `latestRequestId` per dashboard (the most recently submitted)
2. On every `RequestCompleted`/`RequestFailed`: check if `msg.requestId === latestRequestId`. If not, discard the response (it is stale).
3. (Optional) Cancel superseded requests via `Hub.CancelRequest(staleRequestId)` to save backend cycles.

**Why client-side rather than server-side**:
- The server cannot know which of two valid requests the user intends to be "current" — that is client state
- Client-side dedup is O(1) — just a string comparison
- Cancelling stale requests is an optimization, not a correctness requirement

**Caveat**: in slow-render dashboards (5s+), the user may briefly see flashing data if response A arrives and renders before being superseded by B's response. Frontend can mitigate by showing a loading indicator while `latestRequestId` has no result yet.

**Implementation note (Phase 7)**: include the `latestRequestId` pattern in `samples/DotnetTestClient` so the frontend team sees the canonical implementation.

---

## OQ-P10-A — AdapterRequest.ParentRequestId placement

**Decision**: Added `ParentRequestId`, `UserId`, and `ParentDeadline` directly to `AdapterRequest` as nullable fields.

**Alternative rejected**: A separate `AdapterContext` wrapper that all call sites pass alongside the request. Rejected because it would change every adapter call site (SQL adapters, dropdown fetch) for fields they never use. Adding nullable fields to the existing record has zero impact on SQL adapters.

**Implication**: SQL adapters ignore the three new fields. `ExternalProviderAdapter` reads them. Any future adapter type follows the same pattern.

---

## OQ-P10-B — ProviderId hint in envelope

**Decision**: Include `providerId` as an optional field in `ExternalProviderConfig` (`connectionConfig.providerId`). The adapter currently does NOT pass it to the nested `RequestEnvelope` because `RequestEnvelope` has no `ProviderId` field. If Bridge-side routing by `providerId` is needed, the field in `OperationRequestMessage` must be added in a future phase.

**Status**: `connectionConfig.providerId` is parsed and stored but not yet used. Deferred to Phase 11 (Bridge routing enhancement).

---

## OQ-P10-C — Nested timeout vs cache TTL independence

**Decision**: `timeoutMs` in `connectionConfig` governs fetch deadline only. Widget `cacheSeconds` governs cache TTL only. These are independent: a slow operation can be cached for a long time; a fast operation can have a short cache.

**Implication**: `ExternalProviderAdapter.FetchAsync` computes `effectiveTimeout = min(config.timeoutMs, parentRemaining)`. Cache store/eviction is entirely handled by `DashboardResolver` and `WidgetCacheService`, not by the adapter.

---

## OQ-P10-D — ExternalProviderConfig validation at Resolve() vs FetchAsync()

**Decision**: Validate `operationName` and `paramMapping` presence at `DatasourceAdapterFactory.Resolve()` — fail fast at dashboard load time (when the resolver first resolves the adapter), not per-render.

**Reasoning**: `Resolve()` is called once per widget per render. Config errors should surface immediately as `PROVIDER_CONFIG_INVALID` at the widget level, not silently succeed until the first actual data fetch. Per-render failure would also make the error harder to distinguish from a network/provider failure.

**Implication**: `ExternalProviderAdapter.FetchAsync` still has a secondary parse-and-catch for malformed JSON (defense-in-depth), but structural validation (required fields) is the factory's responsibility.

---

## Dashboard version field semantics

The `version` field on `DashboardRenderPayload` increments every time an admin saves changes to the dashboard definition (via `metadata.dashboards.upsert`).

**Backend behaviour**:
- Each save bumps `version` by 1 (atomic increment in PostgreSQL)
- `version` is part of the cache key for the dashboard's resolved payload: `widget:{tenant}:{dashCode}:v{version}:{widgetId}:{filtersHash}`
- When `version` bumps, old cache keys naturally become unreachable and expire on TTL (no explicit invalidation needed for Redis widget cache)
- `metadata.dashboards.upsert` MUST atomically increment `version` and publish Redis pub/sub `cache-invalidate:dashboard:{code}` to bust in-memory L0 caches on app nodes

**Frontend behaviour (recommended)**:
- Cache dashboard render payloads keyed by `(dashboardCode, version, JSON.stringify(filters))`
- On receiving a payload with a higher `version` than cached, invalidate ALL cache entries for that `dashboardCode` (the definition has changed; old structural assumptions may be invalid)
- A `version` mismatch can also be detected if the user has the dashboard open across tabs and another tab triggered a save — handle gracefully (reload widgets, show a "Dashboard updated" toast)

**WidgetStale interaction**:
- `WidgetStale` (data-driven invalidation from events) does NOT bump `version` — it signals data freshness, not structural change
- `version` bumps are about definition changes (new widget added, datasource changed, layout modified, etc.)

**Implementation note (Phase 4)**: Resolver MUST read `version` from `report_definitions` and include it in widget cache keys. `metadata.dashboards.upsert` handler must atomically: (1) increment `version`, (2) write to `report_definitions`, (3) publish `cache-invalidate:dashboard:{code}` pub/sub.

---

## Coding standards (locked for Phase 2 onward)

Locked at Phase 2 start. Apply to every project in the solution.

### Project-level settings (every `.csproj`)
```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

### Type shape
- `record` for immutable DTOs (all Contracts types, queue messages, store records, response envelopes)
- `class` for stateful types (validators, caches, services, MassTransit consumers)
- `sealed` on all records and classes that are not designed for inheritance
- Property init style for records (`public required string X { get; init; }`) — NOT primary constructor style. Reason: readable at scale, compatible with `required`, works cleanly with source-gen STJ.

### Visibility
- `public` for all types in `Shared/Contracts` (they are the integration surface)
- `internal` for implementation helpers within any other Shared/* project
- No `protected` on records; prefer composition over inheritance for DTOs

### UUID generation
- Use `Guid.CreateVersion7()` for all UUID v7 generation — never `Guid.NewGuid()`, never a custom implementation

### JSON serialization
- All JSON via source-generated `JsonSerializerContext` from day 1 — no `JsonSerializer.Serialize(obj)` with reflection-based options
- Per-property `[JsonPropertyName]` ONLY when the C# identifier genuinely differs from the JSON wire name (e.g. an acronym or reserved keyword). Not for camelCase — that is handled globally by `PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase` in the context.
- Enum serialization: source-gen context uses `UseStringEnumConverter = true` + `PropertyNamingPolicy = CamelCase`. No per-enum `[JsonConverter]` attributes.

### MessagePack
- `[MessagePackObject]` + `[Key("camelCaseName")]` string keys on types pushed server → client via SignalR
- Types received client → server (Hub method args) do NOT need `[MessagePackObject]`; SignalR's resolver handles deserialization

### Timestamps on Redis store records
- Use ISO 8601 UTC string format (e.g. `"2026-05-18T10:00:00.000Z"`) — not `DateTimeOffset`, not `long` Unix ms
- Conversion to/from `DateTimeOffset` happens at the boundary (store entry/exit), not in the record itself
- **Rationale**: human-readable in `redis-cli`, language-agnostic for future polyglot tooling, consistent with wire contracts in PROTOCOL.md and with `QueuedAt: string` in `SubmitAck` (Option B)
- Applied to: `OwnerStoreRecord.SubmittedAt`, `IdempotencyRecord.CreatedAt`, `ResultStoreRecord.*`, `ProgressEvent.Timestamp`

### Comments
- Default: no comments
- Add a comment only when the WHY is non-obvious: a hidden constraint, an invariant not visible from the type system, a workaround for a specific external behavior
- Never comment WHAT the code does

### CVE / vulnerability policy

- NU1902 warnings (vulnerable packages) MUST be treated as build errors via `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (already enforced)
- Run `dotnet list package --vulnerable --include-transitive` at the start of each Phase
- Bump immediately when patch versions are available — do not defer security patches across phase boundaries
- Patches that bump major versions require a brief compatibility review note in the phase plan

### Secret hashing

- **Provider `clientSecret`**: BCrypt work factor 12 (~250ms verify cost). Package: `BCrypt.Net-Next`. `BCrypt.HashPassword(secret, workFactor: 12)` at registration time; `BCrypt.Verify(clientSecret, storedHash)` at authentication time. The deliberate slowness resists offline brute force if `provider_registry` is ever exfiltrated.
- **User passwords**: NEVER stored. Authentication is federated via external IdP issuing JWTs. This platform is not an identity provider.
- **JWT signing keys**: managed in Phase 8 (Provider Bridge). Separate from this decision.

---

## Markdown rendering policy

Applies to `TextWidgetTransformer` (Phase 4) and any future markdown rendering surface.

- **Markdown library**: Markdig — pinned to an exact patch version (NOT a range like `0.40.x`). Current pin: `0.37.0`. Re-pin only when a patch release addresses a CVE or a required feature; record the change reason here.
- **HTML sanitization**: HtmlSanitizer — pinned to an exact patch version. Run `dotnet list package --vulnerable` at each phase start to verify.
- **Pipeline order**: substitute placeholders → Markdig render → HtmlSanitizer (order is a security invariant — sanitizer always runs last)
- **Filter value escaping**: HTML-encode user-supplied filter values BEFORE placeholder substitution so that a filter value of `<script>` becomes `&lt;script&gt;` in the rendered markdown, not executable HTML
- **CVE re-verification**: at each phase start (`dotnet list package --vulnerable --include-transitive`)
- **Allowed HTML after sanitization**: standard text/formatting elements, `<a href="https?:">`, `<img src="https?:">`. Blocked: `<script>`, `<style>`, `on*` event attributes, `javascript:` URIs, `data:` URIs, `<iframe>`, `<object>`, `<embed>`

---

## Integration tests — deferred execution

### Status

**Phase 3 deferred tests:**
- **T6** (concurrent reads under reload): **executed and passing** — refactored to use `FakeOperationRegistry`; no Docker required. `FakeOperationRegistry` uses the extracted `RegistrySnapshot` type and identical `Volatile.Read`/`Volatile.Write` pattern as `PostgresOperationRegistry`, so the test verifies the production design.
- **T7** (Redis pub/sub triggers reload): **code-reviewed; execution deferred to Phase 12**
- **T8** (invalid schema graceful skip): **code-reviewed; execution deferred to Phase 12**

**Phase 4 deferred tests:**
- **`DashboardResolver_PostgresAdapter_RealQuery`**: integration test that wires the full stack — `SqlQueryBuilderAdapter` → real Npgsql connection → `DashboardResolver.RenderAsync` — and asserts correct rows are returned. Requires a live PostgreSQL database via Testcontainers. **Code design reviewed; execution deferred to Phase 12.**

### Why deferred

Phase 3 and Phase 4 development environments did not have Docker available. T7, T8, and `DashboardResolver_PostgresAdapter_RealQuery` require Testcontainers (Redis + PostgreSQL). All deferred tests were code-reviewed for correctness against their respective plan documents.

### Gate for Phase 12

The Phase 12 (Validation & deliverables) plan MUST include all three deferred tests:
- **T7**: `Providers.Tests.Registry.ProviderRegistryTests.T7_RedisPubSubTriggersReload`
- **T8**: `Providers.Tests.Registry.OperationRegistryReloadTests.T8_InvalidSchemaInDb_GracefulSkip_ValidRegistrationsReachable`
- **`DashboardResolver_PostgresAdapter_RealQuery`**: `Resolver.Tests.Core.DashboardResolverTests.DashboardResolver_PostgresAdapter_RealQuery` — seeds `queryable_sources` row, runs `DashboardResolver.RenderAsync`, asserts non-empty rows
- All must pass before declaring Phase 12 complete
- If any fail, fix in-phase before declaring done
- Tests are tagged `[Trait("Category","Integration")]` + `[Trait("RequiresDocker","true")]`; run with `dotnet test --filter "RequiresDocker=true"`

### Why T6 was NOT deferred

T6 verifies the core concurrency invariant of `IOperationRegistry`: no torn state under concurrent reads during snapshot swap. It is the highest-risk test of Phase 3 — a bug here means production stale-reads or `NullReferenceException` under load. It does not inherently require Docker; the snapshot pattern is entirely in-memory. Refactoring to `FakeOperationRegistry` preserves verification value while removing the Docker dependency.

---

## Phase 2 work items (surfaced during Phase 1.5 review)

The following implementation requirements were clarified during the documentation review and must be tracked for the relevant phases:

### SSE progress event buffer (Phase 7 — `Progress.Dispatcher.Worker`)

Fix 3 in the Phase 1.5 review established a **binding requirement**: the backend must buffer up to 100 progress events per `requestId` for up to 30 seconds before any SSE client connects. This covers the race window where a client opens SSE slightly after the first progress event was emitted.

**Implementation**: when implementing `Progress.Dispatcher.Worker`, the Redis pub/sub channel `sse-progress:{requestId}` must maintain a small ring buffer. Recommended approach: Redis Stream (`XADD` with `MAXLEN ~ 100`) with a 30-second TTL on the stream key. SSE endpoint reads backlog on connect (`XRANGE`) then switches to live `XREAD`.

---

## Handler unit-test coverage policy (Phase 5)

Of the 18 operation handlers shipped in Phase 5, 7 have dedicated per-handler unit-test suites covering their non-trivial paths:

| Handler | Tests | Why tested |
|---------|-------|------------|
| `DashboardRenderHandler` | 2 | Progress reporting, mandatory-param guard |
| `WidgetTableExportHandler` | 4 | CSV/XLSX bytes, row-limit enforcement, format guard |
| `WidgetDrillContextHandler` | 8 | Token resolution grammar (3 scopes), mismatch, missing fields |
| `WidgetFilterOptionsHandler` | 3 | Static vs adapter paths, search filter |
| `MetadataDashboardUpsertHandler` | 4 | Version increment, cache invalidation, E2E L0 eviction |
| `OperationDispatcher` | 7 | All failure modes + progress drain |
| `RequestSubmissionService` | 5 | End-to-end submit + idempotency |

The remaining 11 handlers (`DashboardListHandler`, `DashboardGetHandler`, all three Datasource handlers, `MetadataDashboardDeleteHandler`, `MetadataDatasourceUpsertHandler`, `MetadataDatasourceDeleteHandler`, `MetadataSchemaUpsertHandler`, `AdminProvidersReloadHandler`, `AdminCacheFlushHandler`) are simple delegators: resolve params, call one repository/registry method, `SerializeToElement`, return. They contain no branching logic beyond the `INVALID_PARAMS` guard in `AdminCacheFlushHandler`. Per-handler unit tests would be ~400 lines of identical fake-repository boilerplate with no meaningful invariant to assert.

**Decision**: these 11 handlers are not tested at the unit level. Their correctness relies on:
1. The `OperationDispatcher` integration tests (handler resolved + dispatched correctly)
2. The `OperationsExtensions` DI registration (all 18 handlers registered)
3. The Postgres repository implementations (covered by Phase 12 integration tests)

If a handler gains branching logic in a future phase, per-handler tests must be added at that point.

---

## Worker architecture decision

Single `Operation.Router.Worker` handles all operation types via `OperationDispatcher`.

**Rationale**:
- v1 target: ~hundreds req/s — single worker with 3 priority queues sufficient
- Operation isolation already achieved via priority queues (admin/metadata typically low; dashboard query normal/high)
- Simpler ops surface: 1 service to deploy, monitor, scale
- DI complexity contained: all handlers in one composition root

**Decision rejected**: separate `Query.Worker` / `Metadata.Worker` / `Admin.Worker`.
Reason: speculative isolation without measurement. Extract later if production traffic shows specific worker types interfering with each other.

**Re-evaluation triggers** (when to revisit):
- Metadata operation p99 > 500 ms sustained (DB write contention)
- Admin operation causing Query throughput drops > 20%
- Per-worker horizontal scaling needed (e.g., 10× Query workers but 1× Metadata)
- Different VM/container resource profiles required per operation type

---

## DLQ inspection (Phase 6 producer, Phase 11 consumer)

Phase 6 Router produces dead-lettered messages (exchange `operation.request.dlq`, queue
`op-request-dlq`) but does not consume from them.

Phase 11 will implement three admin operations:
- `admin.dlq.list` — paginated list of DLQ messages with metadata (operation, requestId, tenantId, failureReason, enqueuedAt)
- `admin.dlq.replay` — re-enqueue selected messages to original priority queue with a fresh `requestId`
- `admin.dlq.discard` — permanent delete (with audit log entry)

Until Phase 11: ops monitors `op-request-dlq` queue depth via RabbitMQ Management UI
(port 15672). Grafana alert fires when queue depth > 0 for > 5 minutes.

**Why not Phase 6**: Phase 6 is pure plumbing (produce DLQ messages). DLQ consumption is
an admin/ops function more naturally grouped with the `admin.*` handler family delivered
in Phase 11 alongside other administrative operations.

---

## Object storage — deferred to Phase 11

**Question (Q4):** Which object storage provider handles large exports (widget.tableExport > 5 000 rows), generated PDF reports, and any binary artefacts produced by handler pipelines?

**Phase 5 decision**: Deferred. The `WidgetTableExportHandler` intentionally throws `LARGE_EXPORT_NOT_SUPPORTED` (code `LARGE_EXPORT_NOT_SUPPORTED`) for datasets > 5 000 rows. The 5 000-row inline limit is enforced today and ships in Phase 5.

**Phase 11 target**: Define and implement `IObjectStorageClient` with two concrete adapters:
- **Local / dev**: MinIO (S3-compatible, single Docker container) — zero AWS dependency, runnable offline.
- **Production**: AWS S3 (primary) or Azure Blob Storage (alternate, switchable via config). Provider chosen at deploy time via `ObjectStorage:Provider = s3 | azureblob | minio`.

**Interface contract (Phase 11 to define)**:
```csharp
public interface IObjectStorageClient
{
    Task<Uri>   UploadAsync(string key, Stream data, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);
    Task        DeleteAsync(string key, CancellationToken ct = default);
    Task<bool>  ExistsAsync(string key, CancellationToken ct = default);
}
```

**Large-export flow (Phase 11)**:
1. `WidgetTableExportHandler` streams rows to `IObjectStorageClient.UploadAsync` → receives a pre-signed URL (TTL = 1 hour).
2. Returns `TableExportResult { ContentBase64 = null, DownloadUrl = "https://...", SizeBytes, Format }`.
3. Frontend opens `DownloadUrl` directly — no backend proxy hop.

**Security invariants**:
- Pre-signed URLs must include a TTL; backend never generates permanent public URLs.
- Keys are namespaced `exports/{tenantId}/{requestId}/{fileName}` to prevent cross-tenant access.
- IAM policy for production S3 must grant `PutObject` + `GetObject` on the exports prefix only — no `DeleteObject` from the app role (lifecycle policy handles cleanup).

**Why not Phase 5**: no CI/CD pipeline for MinIO Testcontainers yet; handler interface contract not finalised; Phase 11 is the designated integration & storage phase.

---

---

## Phase 7 — Gateway design decisions

### Issue X: `RequestSubmissionService.SubmitAsync` signature (confirmed)

**Confirmed signature** (read from `Shared/Operations/Dispatcher/RequestSubmissionService.cs` after Phase 5):
```csharp
public async Task<SubmitAck> SubmitAsync(
    RequestEnvelope envelope,
    string? connectionId,
    CancellationToken ct = default)
```

`SubmissionContext` **does not exist** as a type anywhere in the codebase. Early planning notes mentioned it as a canonical pattern, but it was never implemented — `connectionId` is passed as a plain `string?`. The Phase 7 plan's invocation patterns in §2.2, §3.1, and §4.1 all use `SubmitAsync(envelope, connectionId, ct)`, which matches the actual signature exactly.

**Phase 7 additions** to this method (documented in PHASE_7_PLAN.md §4.2):
- `OwnerStore` dependency (new constructor parameter)
- `IDatabase redis` dependency (new constructor parameter — for submission log + active-progress Set)
- Owner record write (Step 6b)
- Submission log write (`rp:sublog:{requestId}`, TTL = 30 min)
- Active-progress SADD (`rp:active-progress`, when `options.progress: true`)
- `ProgressStreamUrl` corrected to `/sse/requests/{requestId}/progress` (was `/api/v1/progress/{requestId}`)

---

### Phase 7 open questions — all resolved

| # | Decision |
|---|---|
| OQ1 | **`Progress.Dispatcher.Worker` is a standalone process** — separate service, not embedded in `Request.Api`. Redis pub/sub hop is negligible; service boundary enables independent horizontal scaling of progress relay without scaling the HTTP API. |
| OQ2 | **`Shared/HubContracts`** — new shared project containing `IMainHubClient`, `ResponseDispatchPushMessage` (MessagePack-annotated), and the `MainHub` forward declaration. `Response.Dispatcher.Worker` references `Shared/HubContracts`, not `Realtime.Hub`. `IHubContext<MainHub, IMainHubClient>` provides compile-time type safety on all push method names — no string `"RequestCompleted"` anywhere in production code. |
| OQ3 | **`AddPlatformAuth` in `Shared/Telemetry`** — lives as `AuthExtensions.cs` in the existing telemetry assembly. Extracted to a new `Shared/Auth` project in Phase 11 when API-key authentication is added. |
| OQ4 | **Independent rate limits per service** — `Request.Api` and `Realtime.Hub` each enforce their own sliding-window limits. Cross-transport unified rate limiting (single Redis counter for HTTP + Hub combined) is deferred to Phase 11. Accepted risk: a user could submit 100/min via HTTP + 100/min via Hub = 200/min combined. Acceptable for Phase 7. |
| OQ5 | **SSE heartbeat every 30s** — `ping` event (empty data) sent every 30 seconds to prevent proxy/load-balancer idle disconnection. Implemented as a parallel `Task.Delay(30s)` loop in the SSE handler writing `: ping\n\n` (SSE comment) to the response stream. |

---

### Phase 7 — Strongly-typed Hub push (no string method names)

`MainHub` extends `Hub<IMainHubClient>` (defined in `Shared/HubContracts`). This eliminates all string-based method invocations on the SignalR response path:

- **Before (unsafe)**: `await _hubContext.Clients.Client(id).SendAsync("RequestCompleted", push, ct)`
- **After (safe)**: `await _hubContext.Clients.Client(id).RequestCompleted(push)`

The compiler catches typos, parameter count mismatches, and type mismatches at build time. Any rename of a push method is caught as a `CS0117` build error across all call sites.

---

### Phase 7 — GET /result uniform response envelope

`GET /api/v1/requests/{id}/result` returns a consistent JSON envelope for all 3 outcomes:

```json
// 200 OK
{ "status": "completed", "requestId": "...", "result": { /* ResponseDispatchMessage */ } }

// 202 Accepted
{ "status": "in_flight", "requestId": "...", "submittedAt": "ISO-8601" }

// 404 Not Found
{ "status": "orphaned"|"not_found", "requestId": "..." }
```

Clients branch on `status` string, not HTTP status code. This makes the API contract stable against future HTTP status code changes and simplifies frontend branching logic.

---

### Phase 7 — Orphan detection via submission log

Server-side orphan detection (`GET /result` returning `{ "status": "orphaned" }`) requires a third Redis artifact beyond the owner-store record and idempotency key. Both of those expire too early relative to the orphan detection window.

**Decision**: write `rp:sublog:{requestId}` (TTL = `MessageTtlMs × 3` = 30 min) in `RequestSubmissionService.SubmitAsync` alongside the idempotency key. This key is a simple existence marker — its presence proves the request was submitted. Its absence (after 30 min) means the request was either never submitted or orphaned beyond the detection window.

`OrphanDetector.CheckAsync` returns:
- `"orphaned"` — submission log key exists (submitted, result lost within detection window)
- `"not_found"` — submission log key absent (never submitted, or > 30 min ago)

---

### Admin probe endpoint (Phase 8 — Provider Bridge)

`POST /api/v1/admin/providers/{id}/probe` performs a synthetic Hello/Welcome handshake to verify provider connectivity without requiring the provider's JWT. Returns `{ tlsHandshake, jwtAccepted, welcomeReceived, latencyMs }`.

Referenced in `docs/PROVIDER_ONBOARDING.md §5.2` (Option C) and `docs/PROTOCOL.md §8.7`. Must be implemented as part of the admin endpoint suite alongside Provider Bridge (Phase 8).

---

## Phase 8 — Provider Bridge design decisions

### OQ-C: External operation routing — Option C approved

**Finding**: `Operation.Router.Worker` (Phase 6) has NO routing to `q.provider.{providerId}` queues. Three priority queues (`op-request-high/normal/low`) receive all `OperationRequestMessage`s. `OperationDispatcher` resolves handlers from `OperationHandlerRegistry`, which contains only internal handlers. External operations (HandlerType=external) would return `HANDLER_NOT_FOUND`.

**The data was already available**: `OperationRegistration` has `HandlerType` and `ProviderId` fields, both resolved at Step 1 of `RequestSubmissionService.SubmitAsync`. The routing key choice at Step 8 simply didn't use them.

**Decision (Option C — pre-route at submission time)**:
- `RequestSubmissionService.SubmitAsync` checks `registration.HandlerType` at Step 8
- If `"external"` and `ProviderId` is non-null: routing key = `$"provider.{registration.ProviderId}"`
- Otherwise: existing priority-based routing key (`operation.request.high/normal/low`)
- Published to same `operation.request` exchange — no exchange topology change
- `Provider.Bridge`'s `ProviderRequestConsumer` declares and binds `q.provider.{providerId}` to `operation.request` exchange with routing key `provider.{providerId}` on session start

**Why not Option A/B** (modifying Operation.Router.Worker): Both add an extra message hop through the Router Worker — increased latency, changed Phase 6 service, forwarding consumer complexity. Option C is one conditional in `RequestSubmissionService` with routing data already present.

**No changes to**: `Operation.Router.Worker`, `OperationDispatcher`, `OperationHandlerRegistry`, `IOperationBus`, `MassTransitOperationBus`.

### Queue argument mismatch on re-declare

`ProviderRequestConsumer` wraps `channel.QueueDeclare(...)` in `try/catch` for `OperationInterruptedException` (PRECONDITION_FAILED — 406). On catch: log warning and continue — the existing queue is used as-is. Manual queue delete or a runtime migration is required when TTL or DLX arguments change between deploys. This is intentional: silently failing to redeclare is safer than crashing the session on reconnect.

### JWT probe differentiation

Probe JWTs issued by `POST /api/v1/admin/providers/{id}/probe` carry an additional claim `purpose: "probe"` with `exp = iat + 60` (maximum 60-second TTL). `JwtValidationInterceptor` enforces:
- `purpose == "probe"` AND `Hello.supportedOperations.Length > 0` → `INVALID_ARGUMENT` (probe cannot serve real operations)
- `purpose` absent or `!= "probe"` AND `Hello.supportedOperations.Length == 0` → `INVALID_ARGUMENT` (real provider must declare at least one operation)
- Probe JTI audit logged with `action = 'probe'` in `provider_credentials_audit`

### Pending hash cleanup — defense-in-depth

Short-circuit order in `ValidateCredentialsAsync` during rotation grace: `pending_secret_expires_at > UtcNow` is evaluated BEFORE calling `BCrypt.Verify` on the pending hash. If grace has expired, the second BCrypt call is skipped entirely (DoS mitigation — avoids 500ms cost from an unauthenticated caller who knows rotation is in progress).

A background `PendingHashCleanupService` (`IHostedService`) runs every 5 minutes and clears stale pending hashes:
```sql
UPDATE provider_registry
SET pending_client_secret_hash = NULL, pending_secret_expires_at = NULL
WHERE pending_secret_expires_at IS NOT NULL
  AND pending_secret_expires_at < NOW() - INTERVAL '5 minutes';
```
The extra 5-minute buffer beyond `pending_secret_expires_at` ensures no race with an active verification that loaded the row just before the scheduled update ran.

---

## Phase 11 — Event Ingestion & YARP Gateway decisions

### OQ-P11-A — event_subscriptions sync on dashboard upsert only

**Decision**: Sync on dashboard upsert only (not datasource upsert). Subscriptions are widget-level (`WidgetDefinition.SubscribesTo`); widgets live in dashboards, not datasources. Datasource changes do not affect which events target which widgets.

### OQ-P11-B — SubscribesTo exact match only (v1)

**Decision**: `WidgetDefinition.SubscribesTo` uses exact string matching in Phase 11. Wildcard patterns (e.g. `"order.*"` matching `"order.shipped"`) require pattern-matching in the `event_subscriptions` lookup — deferred to Phase 12.

**Implication**: widget definitions must list exact event types. `"order.shipped"` and `"order.cancelled"` must each appear as a separate entry.

### OQ-P11-C — Same JWKS for all token types

**Decision**: Ingestion tokens use the same JWKS endpoint as user auth tokens (same IdP). Token type is distinguished by the `scope` claim: `scope: "ingestion"` for ingestion tokens, `scope: "user"` for user auth tokens. `Ingestion.Api` checks scope; user-facing routes check different scopes. No separate JWKS needed.

### OQ-P11-D — Gateway dev port 5500

**Decision**: YARP Gateway dev port = `5500`. Provider Bridge (`5400`) and Gateway are separate services — no port conflict risk in production (different containers), but dev port collision avoided by choosing `5500`. Update `docker-compose.yml` and local dev appsettings accordingly.

### OQ-P11-E — Progress re-published verbatim

**Decision**: Progress events forwarded from nested provider to parent SSE stream are re-published verbatim — no enrichment or modification of the `RedisValue` payload. `Request.Api`'s `ProgressPubSubSubscriber` already fans out by channel; no enrichment needed.

### OQ-P11-F — L0 cache invalidation: Option A selected

**Decision**: Option A — proactive L1 Redis DEL + L0 auto-expiry.

Worker publishes `rp:cache-invalidate:widget:{tenantId}:{dashCode}:{widgetId}` to Redis pub/sub. `Request.Api` subscribes on startup; on message receipt, calls `WidgetCacheService.EvictWidgetFromL1Async` (Redis SCAN pattern `widget:{tenantId}:{dashCode}:v*:{widgetId}:*` + DEL).

**L0 staleness window**: Up to 30 seconds. `IMemoryCache` does not support prefix eviction; L0 entries expire via promoted-entry TTL. Accepted: WidgetStale guarantees eventual consistency, not instantaneous L0 coherence.

**Implementation cost**: ~30 lines across 2 files. Confirmed < 1 day.

---

### X-Tenant-Id Trust Boundary Invariant (Patch 3)

**ID**: OQ-P11-X-TenantId-Trust

**Invariant**: Backend services MUST extract tenant identity from the **JWT**, never from `X-Tenant-Id` / `X-User-Id` headers for authorization decisions. The Gateway-forwarded headers are **informational only**.

```csharp
// ✅ CORRECT — identity from JWT (cryptographically signed)
var tenantId = User.FindFirstValue("tenant_id")
    ?? throw new UnauthorizedAccessException("tenant_id claim missing");

// ❌ WRONG — header is informational; could be spoofed if direct access bypasses gateway
var tenantId = Request.Headers["X-Tenant-Id"];
```

**Code-review enforcement**: Any PR where `tenant_id` (or equivalent authorization identity) is read from `Request.Headers["X-Tenant-Id"]` in an authorization path is a **blocking review comment**. Only `User.FindFirstValue("tenant_id")` / `JWT.GetTenantId()` is acceptable.

**Why**: Defense-in-depth. Even though backends are not directly reachable (network policy), authorization decisions must be based on cryptographically signed artifacts. This also ensures correctness if a backend is temporarily directly exposed (debugging, misconfiguration).

**Applied to**: `Ingestion.Api.EventIngestionController` (tenantId from `User.FindFirstValue("tenant_id")`), `Gateway.Program` (rate limiting from JWT claim, not header). All future backend services must follow this invariant.

---

### event_subscriptions FK CASCADE (Patch 4)

The `event_subscriptions` table references `dashboard_definitions (tenant_id, dashboard_code)` via a foreign key with `ON DELETE CASCADE`. `dashboard_definitions` has a UNIQUE constraint on `(tenant_id, dashboard_code)` — PostgreSQL allows FK to reference UNIQUE constraints (not only PKs).

**Effect**: When `DELETE FROM dashboard_definitions WHERE tenant_id = @t AND dashboard_code = @c` is executed (by `MetadataDashboardDeleteHandler`), all `event_subscriptions` rows for that dashboard are automatically removed. No application-level cleanup code needed.

**Why not application-level cleanup**: FK CASCADE is atomic (same transaction), simpler, and eliminates a class of orphan-subscription bugs. The alternative (application-level DELETE before dashboard DELETE) requires explicit coordination and is prone to race conditions.

---

### Schema validation caching strategy (Patch 1)

Compiled `JsonSchema` objects from `JsonSchema.Net` are cached in `IMemoryCache` with a 10-minute sliding TTL. Cache key: `"schema:{tenantId}:{eventType}"`.

**Performance**: Per-event validation cost ~1ms. Batch of 1000 events = 1–2s without caching. With caching: ~1ms for the first event per `(tenant, eventType)` pair, near-zero for subsequent events within the TTL window.

**Parallel batch validation**: Deferred to Phase 12. Profile-first approach — measure production batch sizes and latency before adding concurrency complexity.

---

### Late progress event acceptance (Patch 5)

Progress events may arrive on the SSE stream after the terminal event. This is an accepted race condition inherent to distributed pub/sub. Clients MUST ignore progress events received after the terminal event for a given `requestId`. Documented in PROVIDER_PROTOCOL.md §18.2.
