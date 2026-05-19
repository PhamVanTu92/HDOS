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

### Admin probe endpoint (Phase 8 — Provider Bridge)

`POST /api/v1/admin/providers/{id}/probe` performs a synthetic Hello/Welcome handshake to verify provider connectivity without requiring the provider's JWT. Returns `{ tlsHandshake, jwtAccepted, welcomeReceived, latencyMs }`.

Referenced in `docs/PROVIDER_ONBOARDING.md §5.2` (Option C) and `docs/PROTOCOL.md §8.7`. Must be implemented as part of the admin endpoint suite alongside Provider Bridge (Phase 8).
