# Phase 11 — Ingestion API + YARP Gateway + Phase 10 Deferred Items

**Status:** APPROVED  
**Author:** Claude (Sonnet 4.6)  
**Date:** 2026-05-20  
**Depends on:** Phase 8 (Provider Bridge), Phase 9 (Provider SDK), Phase 10 (External Adapter)  
**Estimate:** 4–5 days

---

## 1. Ingestion.Api — Endpoints & Payload Shapes

New service: `Services/Ingestion.Api/` — receives domain events from external systems and routes them into the platform's widget-stale pipeline.

### 1.1 Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/v1/events` | Ingest a single event |
| `POST` | `/api/v1/events/batch` | Ingest up to 1 000 events atomically |
| `GET` | `/health` | Health probe |

Both ingest endpoints require a **Bearer JWT** scoped to `ingestion` (distinct from user-auth and provider-auth tokens). The `tenantId` claim in the JWT determines which tenant's widgets can be targeted — a token for `tenant-A` cannot trigger stale notifications in `tenant-B`.

### 1.2 Single Event Envelope

```json
POST /api/v1/events
Authorization: Bearer <tenant-scoped ingestion token>
Content-Type: application/json

{
  "eventType": "order.shipped",
  "occurredAt": "2026-05-20T10:00:00Z",
  "payload": {
    "orderId": "ORD-9182",
    "customerId": "CUS-441"
  }
}
```

`tenantId` is **not** in the request body — it is extracted from the JWT `sub` or `tenant_id` claim. This prevents cross-tenant spoofing.

The body maps to the existing `IngestEventEnvelope` record (already defined in `Shared/Contracts/Envelopes/IngestEventEnvelope.cs`):
```csharp
public sealed record IngestEventEnvelope
{
    public required string  EventType  { get; init; }
    public required string  TenantId   { get; init; }  // server-filled from JWT
    public required string  OccurredAt { get; init; }  // ISO 8601
    public required JsonElement Payload { get; init; }
}
```

The controller sets `TenantId` from the JWT claim before publishing to RabbitMQ — the caller cannot override it.

### 1.3 Batch Envelope

```json
POST /api/v1/events/batch
Content-Type: application/json

{
  "events": [
    { "eventType": "order.shipped", "occurredAt": "...", "payload": { ... } },
    { "eventType": "inventory.low",  "occurredAt": "...", "payload": { ... } }
  ]
}
```

Hard limit: **1 000 events per batch**. Returns `400` with error code `BATCH_TOO_LARGE` if exceeded. Batch is published atomically via a single MassTransit `PublishBatch` call. Partial failures are not reported per-event — the entire batch either succeeds or returns 400/429/500.

### 1.4 Rate Limiting

- Default: **1 000 events/minute per `tenantId`**
- Configurable via `appsettings.json` → `Ingestion:RateLimits:{tenantId}` (override per tenant)
- Implemented via ASP.NET Core rate limiting middleware (`AddRateLimiter`) with a sliding window policy keyed on the JWT `tenantId` claim
- Returns `429 Too Many Requests` with `Retry-After` header on breach
- Batch counts as N events (size of the batch), not 1 request

### 1.5 Event Schema Validation (Optional)

If a schema is registered for `(tenantId, eventType)` in the `event_schemas` table (§4), the controller validates `payload` against the JSON Schema before publishing. If no schema is registered, payload is accepted as-is. Schema validation failure returns `422 Unprocessable Entity` with error code `EVENT_SCHEMA_VIOLATION`.

### 1.5.1 Schema Validation Cost & Caching (Patch 1)

Per-event JSON Schema validation against `JsonSchema.Net` takes **~1 ms** per event under typical payload sizes (< 10 KB). Worst-case batch of 1 000 events = **1–2 seconds** of synchronous validation on the controller thread.

**Mitigation — compiled schema cache via IMemoryCache:**

```csharp
// Keyed by (tenantId, eventType); eviction TTL = 10 minutes.
// Compiled JsonSchema is cached for its full lifetime once fetched from DB.
// Cache miss = DB read + JsonSchema.FromText(); subsequent calls get cached instance.
private Task<JsonSchema?> GetSchemaAsync(string tenantId, string eventType, CancellationToken ct)
```

- Cache key: `$"schema:{tenantId}:{eventType}"`
- TTL: 10 minutes (sliding). Re-fetched from `event_schemas` table on expiry.
- Compiled `JsonSchema` object is thread-safe and reused across requests.
- Parallel validation across batch items is deferred to Phase 12 pending measurement (profile first, optimize second).

**No-schema fast path**: If `GetSchemaAsync` returns null (no row in `event_schemas`), skip validation entirely — zero overhead per event.

### 1.6 Response Shapes

**Success (201 Created):**
```json
{ "accepted": 1, "eventIds": ["evt_01HQ..."] }
```
`eventId` is a server-generated ULID stamped at ingestion time; logged for audit/debug.

**Error (400/422/429/500):**
```json
{ "error": "BATCH_TOO_LARGE", "message": "Batch limit is 1000 events." }
```

---

## 2. EventEnvelope JSON Shape (Internal Message Bus)

The internal RabbitMQ message is the existing `IngestEventEnvelope` extended with server-stamped fields. No new type is needed — the existing record already fits.

Exchange: `events.raw` (topic exchange, durable)  
Routing key: `events.{tenantId}.{eventType}` — allows selective consumers per tenant or event type in future.

The `Event.Processor.Worker` binds its queue to `events.raw` with routing key `events.#` (all events).

---

## 3. Event.Processor.Worker

New or extended worker service: `Services/Event.Processor.Worker/`

### 3.1 Consumer

Consumes `IngestEventEnvelope` messages from `events.raw` via MassTransit (same pattern as `Operation.Router.Worker`).

### 3.2 Subscription Matching Algorithm

For each consumed event `(tenantId, eventType)`:

1. **Lookup**: Query `event_subscriptions` table (§4.2) for all `(tenantId, event_type)` matches.
   - This is a **direct table lookup**, not a scan of all dashboard definitions.
   - Returns a list of `(dashboard_code, widget_id)` pairs.

2. **For each matching widget**:
   a. Compute SignalR group name: `"widget:{dashboardCode}:{widgetId}"`
   b. Build `WidgetStaleHint { Reason = WidgetStaleReasons.DataUpdated, UpdatedAt = event.OccurredAt }`
   c. Dispatch `WidgetStale` via Redis pub/sub to `rp:hub:*` backplane (cross-node SignalR push).
      - Use `IHubContext<MainHub, IMainHubClient>` injected into the worker.
   d. Optionally: Invalidate `WidgetCacheService` L1 (Redis) cache keys matching this widget.

3. **No match**: Event is silently accepted and discarded — not an error condition.

### 3.3 Cache Invalidation on Stale Signal

When a widget is marked stale, its cached render results (L0+L1) become stale. The worker publishes a Redis `DEL` for the L1 cache key pattern `rp:wcache:{tenantId}:widget:{dashCode}:*:{widgetId}:*`. L0 (IMemoryCache) cannot be invalidated cross-process — frontend re-renders on WidgetStale notification will always hit L1 or trigger a fresh render.

### 3.3.1 L0 Cache Invalidation Strategy — Option A (Patch 2)

**Decision: Option A — proactive L1 Redis DEL + L0 auto-expiry.**

**Reasoning**: Implementation cost is ~30 lines across 2 files — well under 1 day. L1 Redis DEL is the high-value action: it ensures all Request.Api nodes see fresh data on the next render. L0 (IMemoryCache) has a hard **30-second TTL** on promoted entries; since `IMemoryCache` does not support prefix-based eviction, L0 entries are allowed to serve up to 30s of stale data after a WidgetStale signal.

**Implementation:**

**Worker side** — after dispatching `WidgetStale`, publish to Redis:
```csharp
var channel = RedisChannel.Literal(
    $"rp:cache-invalidate:widget:{tenantId}:{dashCode}:{widgetId}");
await _redis.PublishAsync(channel, RedisValue.EmptyString);
```

**Request.Api side** — on startup, subscribe to invalidation channel pattern `rp:cache-invalidate:widget:*`. On message receipt:
```csharp
// Parse tenantId, dashCode, widgetId from channel name.
// Scan Redis for matching L1 keys and delete them.
// Pattern: widget:{tenantId}:{dashCode}:v*:{widgetId}:*
await _widgetCacheService.EvictWidgetFromL1Async(tenantId, dashCode, widgetId);
```

`WidgetCacheService.EvictWidgetFromL1Async` performs a Redis `SCAN` with pattern `widget:{tenantId}:{dashCode}:v*:{widgetId}:*` and issues `DEL` on all matching keys.

**Staleness window**: L0 entries may serve stale data for up to 30 seconds after WidgetStale. This is documented and acceptable — the signal guarantees eventual consistency, not instantaneous L0 coherence. Frontend should display `meta.generatedAt` to communicate data freshness.

**OQ-P11-F**: Resolved → Option A selected.

### 3.4 Subscription Sync

`event_subscriptions` is kept in sync automatically when dashboard definitions are upserted:

- `MetadataDashboardUpsertHandler` (existing) calls a new `EventSubscriptionSyncService` after saving the dashboard.
- `EventSubscriptionSyncService` diffs the new widget definitions' `SubscribesTo` lists against the stored subscriptions and performs targeted `INSERT`/`DELETE` into `event_subscriptions`.
- This keeps the lookup table always consistent with widget definitions without requiring a full scan at event time.

---

## 4. Migration V008 — event_schemas + event_subscriptions

```sql
-- V008__event_ingestion.sql

-- Optional JSON Schema validation per event type per tenant.
CREATE TABLE event_schemas (
    tenant_id    TEXT NOT NULL,
    event_type   TEXT NOT NULL,
    schema_body  JSONB NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (tenant_id, event_type)
);
COMMENT ON TABLE event_schemas IS
    'Optional JSON Schema for validating IngestEventEnvelope.payload per (tenant_id, event_type). '
    'If no row exists, payload is accepted without validation.';

-- Materialized mapping: event_type → widgets that care about it.
-- Kept in sync by EventSubscriptionSyncService on dashboard upsert.
CREATE TABLE event_subscriptions (
    tenant_id      TEXT NOT NULL,
    event_type     TEXT NOT NULL,
    dashboard_code TEXT NOT NULL,
    widget_id      TEXT NOT NULL,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (tenant_id, event_type, dashboard_code, widget_id),
    -- Patch 4: CASCADE ensures orphan cleanup when a dashboard is deleted.
    -- FK references the UNIQUE constraint (tenant_id, dashboard_code) on dashboard_definitions
    -- (PK is BIGSERIAL id; the composite unique constraint supports FK references in PostgreSQL).
    FOREIGN KEY (tenant_id, dashboard_code)
        REFERENCES dashboard_definitions (tenant_id, dashboard_code)
        ON DELETE CASCADE
);
CREATE INDEX idx_event_subscriptions_lookup
    ON event_subscriptions (tenant_id, event_type);
COMMENT ON TABLE event_subscriptions IS
    'Denormalized mapping: when (tenant_id, event_type) arrives, these widgets become stale. '
    'Populated/maintained by MetadataDashboardUpsertHandler via EventSubscriptionSyncService. '
    'ON DELETE CASCADE ensures rows are removed when the parent dashboard is deleted.';
```

### 4.1 WidgetDefinition.SubscribesTo

Add `SubscribesTo` to `WidgetDefinition` (new nullable field, backward compatible):

```csharp
// In Shared/Contracts/Definitions/DashboardDefinition.cs
public sealed record WidgetDefinition
{
    // ... existing fields ...

    /// <summary>
    /// Event types this widget subscribes to for stale invalidation.
    /// When any listed event arrives for this tenant, the widget triggers WidgetStale.
    /// Null or empty = no event-driven invalidation.
    /// </summary>
    public IReadOnlyList<string>? SubscribesTo { get; init; }
}
```

Dashboard definition JSON example:
```json
{
  "widgetId": "orders-chart",
  "subscribesTo": ["order.shipped", "order.cancelled"]
}
```

---

## 5. YARP Gateway — Configuration & Routes

New service: `Services/Gateway/` — Microsoft YARP reverse proxy.

### 5.1 Route Table

| Route ID | Match Path | Cluster | Notes |
|----------|-----------|---------|-------|
| `requests` | `/api/v1/requests/{**catch-all}` | `request-api` | Standard HTTP; no special transforms |
| `events` | `/api/v1/events/{**catch-all}` | `ingestion-api` | New Ingestion.Api |
| `admin` | `/api/v1/admin/{**catch-all}` | `request-api` | Same backend, different path |
| `jwks` | `/.well-known/jwks.json` | `request-api` | Public endpoint, no auth |
| `sse-requests` | `/sse/requests/{**catch-all}` | `request-api` | SSE: `RequestBuffering: false`, long timeout |
| `hub` | `/hubs/main` | `realtime-hub` | WebSocket upgrade required |
| `health-gateway` | `/health` | — | Served by Gateway itself |

### 5.2 YARP Configuration (appsettings.json shape)

```json
{
  "ReverseProxy": {
    "Routes": {
      "requests": {
        "ClusterId": "request-api",
        "Match": { "Path": "/api/v1/requests/{**catch-all}" }
      },
      "events": {
        "ClusterId": "ingestion-api",
        "Match": { "Path": "/api/v1/events/{**catch-all}" }
      },
      "admin": {
        "ClusterId": "request-api",
        "Match": { "Path": "/api/v1/admin/{**catch-all}" }
      },
      "jwks": {
        "ClusterId": "request-api",
        "Match": { "Path": "/.well-known/jwks.json" },
        "AuthorizationPolicy": "Anonymous"
      },
      "sse-requests": {
        "ClusterId": "request-api",
        "Match": { "Path": "/sse/requests/{**catch-all}" },
        "Transforms": [{ "RequestHeadersCopy": "true" }]
      },
      "hub": {
        "ClusterId": "realtime-hub",
        "Match": { "Path": "/hubs/main" }
      }
    },
    "Clusters": {
      "request-api":   { "Destinations": { "primary": { "Address": "http://request-api:5000/" } } },
      "ingestion-api": { "Destinations": { "primary": { "Address": "http://ingestion-api:5100/" } } },
      "realtime-hub":  { "Destinations": { "primary": { "Address": "http://realtime-hub:5200/" } } }
    }
  }
}
```

### 5.3 JWT Validation at Gateway

Gateway validates JWT on all routes except `jwks` and `health`. Validated claims are forwarded to backends as headers:

| Header forwarded | JWT claim | Backends read via |
|-----------------|-----------|------------------|
| `X-Tenant-Id` | `tenant_id` or `sub` (for providers) | `Request.Headers["X-Tenant-Id"]` |
| `X-User-Id` | `sub` | — |
| `X-Token-Scope` | `scope` | Ingestion.Api checks `ingestion` scope |
| `Authorization` | `Bearer <original token>` | Backends can re-validate if needed |

Backend services MUST NOT require Gateway's claim headers for correctness — they still validate the JWT themselves. Gateway validation is a **first-line rejection** (saves backend load), not a replacement for backend validation.

### 5.4 Rate Limiting at Gateway Level

Gateway enforces coarser rate limits than per-service limits:
- **Global**: 10 000 req/min per IP (anti-flood)
- **Per tenant**: 5 000 req/min per `X-Tenant-Id` (cross-service aggregate)
- These are in addition to Ingestion.Api's own 1 000 events/min tenant limit

---

## 6. WebSocket and SSE Routing Specifics

### 6.1 SSE (`/sse/requests/{**catch-all}`)

YARP must NOT buffer the response body — SSE works by streaming chunks indefinitely.

```csharp
// In Gateway Program.cs, configure route metadata:
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.UseSessionAffinity();
    proxyPipeline.UseLoadBalancing();
    proxyPipeline.Use(async (ctx, next) =>
    {
        // Disable response buffering for SSE routes
        if (ctx.Request.Path.StartsWithSegments("/sse"))
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
        await next();
    });
});
```

Additionally, configure the YARP route's `HttpRequest.Timeout` to match the max SSE connection lifetime (e.g. 10 minutes) rather than the default 100s. Backend is responsible for keeping the SSE connection open.

### 6.2 WebSocket (`/hubs/main`)

YARP supports WebSocket proxying natively. No extra configuration needed — YARP detects `Connection: Upgrade` + `Upgrade: websocket` and switches protocols. The gateway must NOT transform or buffer the upgrade handshake.

**SignalR negotiation flow via Gateway:**

```
Client → Gateway /hubs/main/negotiate  (HTTP POST)  → Realtime.Hub
Client → Gateway /hubs/main?id=...     (WebSocket)  → Realtime.Hub
```

Both the negotiate and WebSocket connection go to the same backend (session affinity via `connectionId` is handled by SignalR's own negotiate redirect). No sticky sessions needed at YARP level because the negotiate response tells the client which transport to use, and SignalR handles reconnection.

---

## 7. CORS Configuration

### Decision: Global config with per-environment origin allowlist

**Rejected**: Per-tenant origin allowlist — requires database lookup per preflight. CORS policy is infra-level, not business-level. Frontend teams use a shared origin.

**Chosen**: Origins configured in `appsettings.json` (env-specific):

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://app.platform.example.com",
      "https://staging.platform.example.com"
    ]
  }
}
```

Development: `"AllowedOrigins": ["http://localhost:3000", "http://localhost:5173"]`

The Gateway is the **sole CORS responder** — backend services set `AllowAnyOrigin()` but are not reachable directly (trust boundary). The Gateway's CORS policy:
- `AllowCredentials()` — required for SignalR with auth
- `AllowAnyHeader()` — including `Authorization`
- `AllowAnyMethod()`
- Exposed headers: `Content-Type`, `X-Request-Id`
- Max age: 1 hour (reduces preflight load)

---

## 8. TLS Termination + Backend Trust Model

### 8.1 Production

- **TLS terminates at Gateway** — external traffic on `443`. Certificate managed externally (Let's Encrypt, cloud load balancer, or cert-manager in K8s).
- **Backend services listen on plain HTTP/2** within the cluster trust domain (K8s namespace or Docker network `hdos_platform`). No backend TLS needed — mTLS enforcement is a K8s network policy concern, not application-level.
- **Provider Bridge** is the exception: it has its own `5400` TLS endpoint for external gRPC providers and is NOT behind YARP (per Phase 8).

### 8.2 Development

- Gateway listens on `http://localhost:5400` (no TLS) — same as Phase 8 bridge port conflict — **resolve: Gateway dev port = `5500`**.
- All backends on `http://localhost:{port}` as today.
- `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` in dev startup (same as Phase 9 tests).

### 8.3 Trust Headers

Backends trust `X-Tenant-Id` and `X-User-Id` headers **only from the gateway IP range**. In production: K8s network policy restricts direct access. In dev: no enforcement (local network).

### 8.3.1 X-Tenant-Id Trust Boundary Invariant (Patch 3)

**Invariant**: Backend services MUST extract tenant identity from the **JWT**, never from `X-Tenant-Id` / `X-User-Id` headers for authorization decisions. The forwarded headers are **informational only** — they may be used for logging, diagnostics, or avoiding a second JWT parse, but they are not the authoritative source of truth.

```csharp
// ✅ CORRECT — identity from JWT
var tenantId = User.FindFirstValue("tenant_id")
    ?? throw new UnauthorizedAccessException("tenant_id claim missing");

// ❌ WRONG — header is informational; could be spoofed if direct access bypasses gateway
var tenantId = Request.Headers["X-Tenant-Id"];
```

**Code-review enforcement**: any PR where `tenant_id` (or equivalent authorization identity) is read from `Request.Headers["X-Tenant-Id"]` in an authorization path is a blocking review comment. Only `JWT.GetTenantId()` / `User.FindFirstValue("tenant_id")` is acceptable.

**Why**: Even though backends are not directly reachable from the public internet (network policy), defense-in-depth requires that authorization decisions be based on cryptographically signed artifacts (JWTs), not easily-spoofable HTTP headers. This also ensures correctness if a backend is ever temporarily exposed directly (e.g., for debugging) or if the gateway is misconfigured.

**Decision added to DECISIONS.md as OQ-P11-X-TenantId-Trust.**

---

## 9. Phase 10 Deferred Items

### 9.1 Progress Forwarding for Nested Provider Calls

**Mechanism:**

`ExternalProviderAdapter` already has `ISubscriber`. When `AdapterRequest.ParentRequestId != null` AND `AdapterRequest.ParentWantsProgress == true` (new field), the adapter additionally subscribes to `rp:sse-notify:{nestedId}` and re-publishes each received message to `rp:sse-notify:{parentId}`.

**New field on AdapterRequest:**
```csharp
/// <summary>If true and ParentRequestId is set, nested provider progress events are
/// forwarded to the parent SSE stream. Matches parent OperationHandlerContext.Progress != null.</summary>
public bool ParentWantsProgress { get; init; }
```

**DashboardRenderHandler change:**
```csharp
// Existing call site gains one more param
callerWantsProgress: context.Progress is not null
```

**DashboardResolver passes to AdapterRequest:**
```csharp
ParentWantsProgress = callerWantsProgress,
```

**ExternalProviderAdapter FetchAsync (augmented):**
```csharp
if (request.ParentWantsProgress && request.ParentRequestId is not null)
{
    var progressChannel = RedisChannel.Literal(RedisKeys.SseNotify(nestedId));
    var parentProgressChannel = RedisChannel.Literal(RedisKeys.SseNotify(request.ParentRequestId));
    await _subscriber.SubscribeAsync(progressChannel, async (_, value) =>
        await _subscriber.PublishAsync(parentProgressChannel, value));
    // Unsubscribe in finally block alongside terminal channel
}
```

No new infrastructure needed — `rp:sse-notify:{requestId}` is the existing progress channel written by `ProgressRelayWorker` and consumed by `Request.Api`'s `ProgressPubSubSubscriber`.

**Test:** `EP12_NestedProgress_ForwardedToParentSseChannel` — parent `WantsProgress=true`, provider emits 2 progress events; verify they appear on the parent's notify channel.

### 9.1.1 Late Progress Handling — Acceptable Race (Patch 5)

There is an inherent race condition in the progress forwarding mechanism: a progress event may be forwarded to the parent SSE stream **after** the terminal event has already been dispatched. This occurs when:

1. Terminal event arrives and TCS is set (adapter exits `WaitAsync`)
2. Adapter unsubscribes from `rp:sse-notify:{nestedId}` in the `finally` block
3. A buffered in-flight progress message arrives after the unsubscribe

**Accepted behavior**: Late progress events may appear on the parent stream after the terminal signal. This is a valid and acceptable race.

**Client contract** (documented in PROVIDER_PROTOCOL.md SSE section):
> "Clients SHOULD ignore progress events received after the terminal event for a given `requestId`. The terminal event (`done`, `failed`, `cancelled`) signals that no further meaningful state changes will occur. Progress events received subsequently are late arrivals from in-flight pub/sub and carry no new information."

**Implementation note**: The adapter unsubscribes from the progress forwarding channel in the same `finally` block as the terminal channel. Best-effort unsubscription means a narrow window exists. This is not a bug — the platform guarantees **eventual consistency** of the progress stream, not strict ordering after the terminal.

### 9.2 providerId Routing Hint

**Wire changes (3 layers):**

**Layer 1 — Contracts:**
```csharp
// RequestEnvelope.cs (existing, add optional field)
public string? ProviderId { get; init; }

// OperationRequestMessage.cs (existing, add optional field)
public string? ProviderId { get; init; }
```

**Layer 2 — Operations/Adapters:**
- `ExternalProviderAdapter.BuildEnvelope` sets `envelope.ProviderId = config.ProviderId`
- `RequestSubmissionService` copies `envelope.ProviderId` → `message.ProviderId`

**Layer 3 — Bridge:**
- `ProviderRequestConsumer` checks `message.ProviderId`: if non-null AND a session for that providerId is active, prefer that session's RabbitMQ sub-queue.
- If hint is for a disconnected/suspended provider → log warning, fall back to round-robin across active providers for that operation (existing behavior).
- Session routing is already tracked in Bridge's `SessionRegistry` (Phase 8) — add a `TryGetByProviderId(string providerId, out ProviderSession session)` method.

**Test:** `PB_ProviderIdHint_RoutesToSpecifiedProvider` in `tests/ProviderBridge.Tests/`.

---

## 10. Test Scenarios

### 10.1 tests/Ingestion.Tests/ (NEW)

| ID | Name | Verifies |
|----|------|---------|
| IN1 | `SingleEvent_ValidJwt_Returns201_PublishesToRabbit` | Happy path end-to-end |
| IN2 | `BatchEvent_1000Items_AllPublished` | Max batch size succeeds |
| IN3 | `BatchEvent_1001Items_Returns400_BATCH_TOO_LARGE` | Hard cap enforced |
| IN4 | `RateLimit_ExceedTenantQuota_Returns429_WithRetryAfter` | Sliding window rate limit |
| IN5 | `SchemaValidation_ValidPayload_Accepted` | Registered schema + valid payload |
| IN6 | `SchemaValidation_InvalidPayload_Returns422_SCHEMA_VIOLATION` | Registered schema + invalid payload |
| IN7 | `SchemaValidation_NoSchema_AnyPayloadAccepted` | No schema → no validation |
| IN8 | `JwtTenantScope_TokenTenantA_CannotTargetTenantB` | TenantId always from JWT, not body |
| IN9 | `JwtScope_MissingIngestionScope_Returns403` | `ingestion` scope required |
| IN10 | `EventProcessed_MatchingWidget_WidgetStalePublished` | Event → WidgetStale via IHubContext |
| IN11 | `EventProcessed_NoMatchingWidget_SilentlyDiscarded` | No subscription → no push, no error |
| IN12 | `DashboardDeleted_OrphanSubscriptionsRemoved` | ON DELETE CASCADE removes event_subscriptions rows (Patch 4) |

### 10.2 tests/Gateway.Tests/ (EXTEND Phase 7)

| ID | Name | Verifies |
|----|------|---------|
| GW1 | `Route_Requests_ForwardedToRequestApi` | `/api/v1/requests/submit` → backend |
| GW2 | `Route_Events_ForwardedToIngestionApi` | `/api/v1/events` → correct backend |
| GW3 | `Route_Hub_WebSocketUpgrade_Proxied` | `Connection: Upgrade` forwarded |
| GW4 | `Route_Sse_ResponseBufferingDisabled` | SSE chunks stream without buffering |
| GW5 | `JWT_Invalid_Returns401_BeforeBackend` | Gateway rejects, backend never called |
| GW6 | `JWT_Valid_ClaimsForwarded_AsHeaders` | `X-Tenant-Id`, `X-User-Id` in backend request |
| GW7 | `CORS_Preflight_AllowedOrigin_Returns200_WithHeaders` | CORS headers present |
| GW8 | `CORS_Preflight_DisallowedOrigin_Returns403` | Unknown origin rejected |
| GW9 | `RateLimit_GlobalIpFlood_Returns429` | IP-level limit |
| GW10 | `Health_Returns200_WithoutAuth` | `/health` is unauthenticated |

### 10.3 tests/Adapters.Tests/ (EXTEND Phase 10)

| ID | Name | Verifies |
|----|------|---------|
| EP12 | `NestedProgress_WantsProgressTrue_ForwardedToParentChannel` | Phase 10 deferred: progress forwarding |
| EP13 | `ProviderIdHint_SetInEnvelope_FromConfig` | Phase 10 deferred: `ProviderId` in envelope |

---

## 11. Open Questions

| ID | Question | Default / Recommendation |
|----|----------|--------------------------|
| OQ-P11-A | `event_subscriptions` sync: do we also sync on `MetadataDatasourceUpsertHandler` or only on dashboard upsert? | Dashboard upsert only — subscriptions are widget-level, widgets live in dashboards, not datasources. |
| OQ-P11-B | `SubscribesTo` patterns: exact string match only, or support wildcards (e.g. `"order.*"` matches `"order.shipped"`)? | Exact match for Phase 11. Wildcard matching deferred to Phase 12 (requires pattern-matching in `event_subscriptions` lookup). |
| OQ-P11-C | Gateway JWT auth: same JWKS endpoint as user auth, or separate one for ingestion tokens? | Same JWKS (same IdP). Ingestion tokens distinguished by `scope: "ingestion"` claim. Ingestion.Api checks scope; user auth checks `scope: "user"`. |
| OQ-P11-D | YARP Gateway dev port: avoid collision with Provider Bridge (`5400`) from Phase 8 — confirm dev port `5500`. | Use `5500` for Gateway. Update `docker-compose.yml` accordingly. |
| OQ-P11-E | Progress forwarding: should `rp:sse-notify` messages be re-published verbatim or enriched with `parentRequestId`? | Re-publish verbatim — `Request.Api`'s `ProgressPubSubSubscriber` already knows how to fan out by channel; no enrichment needed. |
| OQ-P11-F | Cache invalidation on WidgetStale: invalidate only the L1 Redis key, or also explicitly clear WidgetCacheService L0 (IMemoryCache)? | **Resolved — Option A (Patch 2)**: Worker publishes to `rp:cache-invalidate:widget:{tenantId}:{dashCode}:{widgetId}`. Request.Api subscribes and calls `EvictWidgetFromL1Async` (Redis SCAN + DEL). L0 relies on 30s auto-expiry (IMemoryCache prefix eviction not supported). See §3.3.1. |

---

## 12. Implementation Order (§11.1–11.3)

1. **Migration V008** — `event_schemas` + `event_subscriptions` tables
2. **Contracts changes** — `WidgetDefinition.SubscribesTo`, `RequestEnvelope.ProviderId`, `OperationRequestMessage.ProviderId`, `AdapterRequest.ParentWantsProgress`
3. **EventSubscriptionSyncService** — sync subscriptions on dashboard upsert (in Operations)
4. **Event.Processor.Worker** — consume `events.raw`, dispatch WidgetStale
5. **Ingestion.Api** — controller, JWT auth, rate limiting, schema validation, publish
6. **YARP Gateway** — routes, JWT validation, CORS, claim propagation
7. **ExternalProviderAdapter updates** — progress forwarding (EP12), ProviderId hint (EP13)
8. **Bridge: SessionRegistry.TryGetByProviderId** — routing hint support
9. **tests/Ingestion.Tests/** — IN1–IN11
10. **tests/Gateway.Tests/ extension** — GW1–GW10
11. **tests/Adapters.Tests/ extension** — EP12–EP13

---

## 13. Out of Scope (Phase 11)

- Wildcard `SubscribesTo` patterns (Phase 12)
- Per-tenant ingestion token issuance UI (admin panel, future)
- Event replay / event store (events are fire-and-forget in Phase 11)
- SI2 Testcontainers integration test from Phase 10 (Phase 12)
- mTLS between Gateway and backends (K8s network policy concern)

---

*Target: 6 new files + ~15 modified files, 25 unit tests (IN1–IN12, GW1–GW10, EP12–EP13).*
