# DECISIONS.md — Architecture & Design Decisions
> Created: 2026-05-18 | Updated after Phase 1.5 review

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

## Phase 2 work items (surfaced during Phase 1.5 review)

The following implementation requirements were clarified during the documentation review and must be tracked for the relevant phases:

### SSE progress event buffer (Phase 7 — `Progress.Dispatcher.Worker`)

Fix 3 in the Phase 1.5 review established a **binding requirement**: the backend must buffer up to 100 progress events per `requestId` for up to 30 seconds before any SSE client connects. This covers the race window where a client opens SSE slightly after the first progress event was emitted.

**Implementation**: when implementing `Progress.Dispatcher.Worker`, the Redis pub/sub channel `sse-progress:{requestId}` must maintain a small ring buffer. Recommended approach: Redis Stream (`XADD` with `MAXLEN ~ 100`) with a 30-second TTL on the stream key. SSE endpoint reads backlog on connect (`XRANGE`) then switches to live `XREAD`.

### Admin probe endpoint (Phase 8 — Provider Bridge)

`POST /api/v1/admin/providers/{id}/probe` performs a synthetic Hello/Welcome handshake to verify provider connectivity without requiring the provider's JWT. Returns `{ tlsHandshake, jwtAccepted, welcomeReceived, latencyMs }`.

Referenced in `docs/PROVIDER_ONBOARDING.md §5.2` (Option C) and `docs/PROTOCOL.md §8.7`. Must be implemented as part of the admin endpoint suite alongside Provider Bridge (Phase 8).
