# PROTOCOL.md — Frontend Integration Bible
> Version: 6.3 | Audience: Frontend team | Last updated: 2026-05-18

This document is the **single source of truth** for all frontend teams consuming the Realtime Reporting Platform. You do not need to read any backend source code. Everything you need to build a complete client is here.

---

## Table of Contents

1. [Overview](#1-overview)
   - 1.1 Purpose and audience
   - 1.2 Architecture summary (client view only)
   - 1.3 Key design principle: everything is async
   - 1.4 Versioning policy

2. [Authentication & Authorization](#2-authentication--authorization)
   - 2.1 User JWT format and required claims
   - 2.2 How to attach JWT (HTTP header + SignalR + SSE)
   - 2.3 Tenant isolation guarantee
   - 2.4 RBAC: roles and per-operation `required_role`
   - 2.5 Token expiry and refresh responsibility

3. [Request Submission](#3-request-submission)
   - 3.1 Path A: `POST /api/v1/requests` — HTTP submission
   - 3.2 Path B: `hub.invoke("SubmitRequest", envelope)` — SignalR Invoke submission
   - 3.3 Both paths: identical backend behavior guarantee
   - 3.4 `GET /api/v1/requests/{id}/result` — reconnection fallback
   - 3.5 `POST /api/v1/requests/{id}/cancel` — HTTP cancel
   - 3.6 `POST /api/v1/ingest/events` / `/batch` — event ingestion
   - 3.7 How to choose between HTTP and Invoke (heuristics)

4. [Request Envelope (RequestEnvelope)](#4-request-envelope)
   - 4.1 Full JSON schema
   - 4.2 Field reference
     - `requestId` — UUID v7, client-generated, idempotency key
     - `operation` — dot-notation string, e.g. `"dashboard.render"`
     - `params` — object, validated against registered JSON Schema per operation
     - `tenantId` — required; validated at gateway
     - `userId` — required; from user JWT
     - `correlationId` — optional UUID for cross-request grouping
     - `options.progress` — `true|false`; opt-in to SSE progress stream
     - `options.cacheSeconds` — override cache TTL; `0` = bypass
     - `options.priority` — `"low"|"normal"|"high"`
     - `options.timeoutMs` — client-requested timeout; capped by registry
   - 4.3 `X-Connection-Id` header — **HTTP path only**; not needed for Invoke
   - 4.4 Idempotency guarantee: same `requestId` → same terminal result (across both paths)

5. [Response Envelopes](#5-response-envelopes)
   - 5.1 `ResponseDispatchMessage` (terminal) — full JSON schema
     - `requestId`, `status`, `operation`, `payload`, `error`, `elapsedMs`, `tenantId`
   - 5.2 `ResponseProgressMessage` — full JSON schema
     - `requestId`, `percent` (1–99), `message`, `tsUnixMs`
   - 5.3 `error` object schema
     - `code` — machine-readable string
     - `message` — human-readable
     - `detailsJson` — structured extra info (nullable)
     - `retryable` — boolean hint
   - 5.4 Standard error codes table

   | Code | Meaning | Retryable |
   |---|---|---|
   | `OPERATION_NOT_FOUND` | Unknown operation string | No |
   | `PROVIDER_UNAVAILABLE` | External provider circuit open | Yes (after delay) |
   | `PROVIDER_DISCONNECTED` | Provider dropped mid-request | Yes |
   | `PROVIDER_SUSPENDED` | Provider administratively suspended | No |
   | `TIMEOUT` | Operation exceeded timeout | Maybe |
   | `BACKPRESSURE` | Queue depth exceeded; system is shedding load | Yes (obey retryAfterMs) |
   | `CANCELLED` | Client cancelled the request | No |
   | `VALIDATION_ERROR` | params failed JSON Schema validation | No |
   | `DUPLICATE_REQUEST` | requestId already submitted | No (check result store) |
   | `RATE_LIMITED` | Too many requests | Yes (obey Retry-After) |
   | `UNAUTHORIZED` | Missing or invalid user JWT | No |
   | `FORBIDDEN` | Insufficient role | No |
   | `INTERNAL_ERROR` | Unexpected platform error | Yes |

6. [SignalR Transport (`/hubs/main`)](#6-signalr-transport)
   - 6.1 Endpoint and protocol (MessagePack, Redis backplane)
   - 6.2 Connection lifecycle: connect → authenticate → store `connectionId`
   - 6.3 **Client → Server methods** (callable via `hub.invoke(...)`)
     - `SubmitRequest(envelope) → SubmitAck` — submit request over WebSocket
     - `CancelRequest(requestId) → void` — cancel via WebSocket
     - `SubscribeWidget(channel) → void` — join widget stale group
     - `UnsubscribeWidget(channel) → void`
   - 6.4 **Server → Client push methods** (never call these; only listen)
     - `RequestCompleted(ResponseDispatchMessage)` — terminal success
     - `RequestFailed(ResponseDispatchMessage)` — terminal failure/timeout
     - `RequestCancelled(ResponseDispatchMessage)` — cancel confirmed
     - `WidgetStale(channel, hint)` — re-render signal
   - 6.5 `SubmitAck` shape: `{ requestId, queuedAt, progressStreamUrl? }`
   - 6.6 Hub errors: thrown as `HubException` with typed `data` field
   - 6.7 Routing: messages delivered to specific `connectionId`; fallback to `userId` group
   - 6.8 Redis backplane — transparent; enables multi-node deployment

7. [SSE Transport (Progress Streams)](#7-sse-transport)
   - 7.1 Endpoint: `GET /sse/requests/{requestId}/progress`
   - 7.2 Only useful when `options.progress: true` in the request
   - 7.3 **Open SSE connection BEFORE submitting the request** (either path)
   - 7.4 Event format
   - 7.5 SSE stream closes after terminal event or 5-minute idle
   - 7.6 Do NOT use SSE for the terminal result — always use SignalR

8. [Operation Catalogue](#8-operation-catalogue)
   - 8.1 How to read this table
   - 8.2 Built-in (internal) operations
   - 8.3 Widget operations detail (`widget.render`, `widget.filterOptions`, `widget.tableExport`, `widget.drillContext`)
   - 8.4 External operations (transparent — same envelope, may have higher latency)
   - 8.5 Chart type switching (client-side, no backend call)
   - 8.6 How to discover available operations

9. [Reconnection Protocol](#9-reconnection-protocol)
   - 9.1 Works identically regardless of which submission transport was used
   - 9.2 Step-by-step reconnection flow
   - 9.3 Result TTL (5 minutes)
   - 9.4 Re-submit idempotency

10. [Cancellation](#10-cancellation)
    - 10.1 Via HTTP: `POST /api/v1/requests/{id}/cancel`
    - 10.2 Via SignalR: `hub.invoke("CancelRequest", requestId)`
    - 10.3 Cancel is best-effort; `RequestCancelled` push confirms
    - 10.4 External provider cancel propagation

11. [Progress Streaming — Client Checklist](#11-progress-streaming--client-checklist)
    - 11.1 Set `options.progress: true`
    - 11.2 Open SSE BEFORE submitting (either transport)
    - 11.3 Handle `percent` 1–99
    - 11.4 Render terminal from SignalR, not SSE
    - 11.5 Close SSE after terminal or component unmount

12. [Rate Limiting](#12-rate-limiting)
    - 12.1 Per-tenant: 500 requests/minute
    - 12.2 Per-user: 100 requests/minute
    - 12.3 `BACKPRESSURE`: HTTP 503 with `Retry-After`; Invoke HubException `BACKPRESSURE` with `retryAfterMs`

13. [Request Validation](#13-request-validation)
    - 13.1 Identical validation for both submission paths
    - 13.2 HTTP: 400 response; Invoke: HubException with same error codes
    - 13.3 `params` validated against registered JSON Schema

14. [OpenTelemetry / Distributed Tracing](#14-opentelemetry--distributed-tracing)
    - 14.1 `traceparent` header (W3C) on all HTTP requests
    - 14.2 Jaeger lookup by `requestId`

15. [Changelog](#15-changelog)

---

## 1. Overview

### 1.1 Purpose and audience
Frontend team building a dashboard UI over this platform. Read this document. Do not read backend code.

### 1.2 Architecture summary (client view only)
```
Client ─ HTTP ──────────────────────► POST /api/v1/requests (submit via HTTP)
       ─ WebSocket (SignalR) ────────► hub.invoke("SubmitRequest", ...) (submit via WS)
       ─ WebSocket (SignalR) ◄────────── RequestCompleted / RequestFailed / WidgetStale (always pushed here)
       ─ SSE ───────────────────────► /sse/requests/{id}/progress (progress only)
```

**Key invariant**: The result ALWAYS arrives via SignalR push — never as the return value of `hub.invoke("SubmitRequest")` and never as the HTTP response body of `POST /requests`. Both submission paths feed the same queue; responses are pushed to the same SignalR connection.

### 1.3 Key design principle: everything is async
No HTTP endpoint returns business data synchronously. `POST /requests` returns `202 Accepted` with a `requestId`. The result arrives later via SignalR.

### 1.4 Versioning policy
Breaking changes increment the major version. Additive changes (new fields, new operations) increment the minor version. Frontend must tolerate unknown fields.

---

## 2. Authentication & Authorization

### 2.1 User JWT format and required claims
```json
{
  "sub": "user-uuid",
  "tenant": "tenant-001",
  "roles": ["viewer", "analyst"],
  "exp": 1716030900
}
```

### 2.2 How to attach JWT

- **HTTP requests**: `Authorization: Bearer <jwt>` header
- **SignalR connection**: pass as access token in connection options:
  ```js
  new HubConnectionBuilder()
    .withUrl("/hubs/main", { accessTokenFactory: () => getJwt() })
    .build()
  ```
- **SSE — browser EventSource clients**: query param `?access_token=<jwt>` is **required**. The browser `EventSource` API does not allow custom headers — there is no way to set `Authorization` from a browser. The backend MUST accept the query param.
- **SSE — server-side or native clients (Node.js, mobile native, curl)**: `Authorization: Bearer <jwt>` header is preferred. Query param also accepted as fallback.

### 2.3 Tenant isolation guarantee
Every request is scoped to the `tenantId` in the JWT. The platform enforces this at every layer — Gateway, workers, cache keys, DB queries. You cannot receive another tenant's data.

### 2.4 RBAC: roles and per-operation `required_role`
Some operations require a specific role (e.g., `"admin"`). If the user's JWT lacks the required role, the response is `FORBIDDEN` — not `UNAUTHORIZED`. Check `error.code` in the terminal response.

### 2.5 Token expiry and refresh
The platform does not refresh your JWT. When it expires, SignalR disconnects (Hub closes the connection). Implement `accessTokenFactory` to return a fresh token on reconnect.

---

## 3. Request Submission

There are **two equivalent paths** to submit a request. The result ALWAYS arrives as a SignalR push (`RequestCompleted` / `RequestFailed` / `RequestCancelled`), regardless of which path you use.

### 3.1 Path A: HTTP Submission

```http
POST /api/v1/requests
Authorization: Bearer <jwt>
X-Connection-Id: <signalr-connectionId>
Content-Type: application/json

{
  "requestId": "01HQ7XXXXXXXXXXXXX",
  "operation": "dashboard.render",
  "params": { "dashboardCode": "sales_2025", "filters": { "year": 2025 } },
  "tenantId": "tenant-001",
  "userId": "user-uuid",
  "options": { "progress": false }
}
```

**Response** `202 Accepted`:
```json
{
  "requestId": "01HQ7XXXXXXXXXXXXX",
  "queuedAt": "2026-05-18T10:00:00Z",
  "progressStreamUrl": null
}
```
(`progressStreamUrl` would be a string URL if `options.progress` was `true`)

**Error responses**:
| HTTP | When |
|---|---|
| `400` | Validation failure — body contains field errors |
| `401` | Missing or invalid JWT |
| `403` | User lacks required role for this operation |
| `409` | `requestId` already submitted (idempotency) |
| `429` | Rate limited — check `Retry-After` header |
| `503` | `BACKPRESSURE` — queue depth exceeded — check `Retry-After` header |

**`X-Connection-Id` header**: value of `hubConnection.connectionId` (available after connected). This tells the server which SignalR connection to push the result to. If omitted (e.g., submitting before SignalR connects), the result is still delivered via user-level push when a connection appears — or is available via `GET /result` fallback.

### 3.2 Path B: SignalR Invoke Submission

```js
// After SignalR connection is established:
const ack = await hubConnection.invoke("SubmitRequest", {
  requestId: "01HQ7XXXXXXXXXXXXX",
  operation: "dashboard.render",
  params: { dashboardCode: "sales_2025", filters: { year: 2025 } },
  tenantId: "tenant-001",
  userId: "user-uuid",
  options: { progress: false }
});
// ack: { requestId, queuedAt, progressStreamUrl }
```

- No `X-Connection-Id` needed — the Hub uses `Context.ConnectionId` (the active WebSocket connection making the Invoke call)
- The `invoke` call **returns the ack** (202 equivalent), NOT the result. The result arrives as a separate push (`RequestCompleted` / `RequestFailed`)
- On error, `invoke` throws a `HubException` with `data` field containing the error:
  ```js
  try {
    await hubConnection.invoke("SubmitRequest", envelope);
  } catch (err) {
    if (err.message === "VALIDATION_FAILED") { /* err.data.errors */ }
    if (err.message === "BACKPRESSURE")      { /* err.data.retryAfterMs */ }
    if (err.message === "DUPLICATE_REQUEST") { /* already submitted */ }
    if (err.message === "RATE_LIMITED")      { /* err.data.retryAfterMs */ }
  }
  ```

### 3.3 Both paths: identical backend behavior guarantee

The server routes both `POST /requests` and `Hub.SubmitRequest` through the **exact same backend service** (`RequestSubmissionService`). They produce:
- The same validation errors
- The same idempotency check (requestId-scoped, cross-transport)
- The same owner-store entry
- The same queue message
- The same backpressure response

**Idempotency is cross-transport**: if you submit `requestId = "X"` via HTTP, then immediately submit the same `requestId = "X"` via Invoke, the second attempt returns `DUPLICATE_REQUEST` regardless of transport.

**CI guarantees this**: every critical test scenario has both an HTTP variant and an Invoke variant. If they diverge, the build fails.

### 3.4 GET /api/v1/requests/{id}/result — reconnection fallback

```http
GET /api/v1/requests/{id}/result
Authorization: Bearer <jwt>
```

| Response | Meaning |
|---|---|
| `200 OK` | Terminal result stored in Redis — body is `ResponseDispatchMessage` |
| `202 Accepted` | Request still in flight — wait on SignalR |
| `404 Not Found` | TTL expired (> 5 min), unknown `requestId`, or orphaned request (see below) |

Works regardless of which submission path was used.

#### Orphan detection rule

A request is **orphaned** if it was dropped by the broker (message TTL expired) after the
idempotency claim also expired, leaving no result in the store and no SignalR push ever
delivered.

**Client rule**: if `GET /result` returns `404` AND the client's local `queuedAt` timestamp
satisfies `now − queuedAt > 20 minutes` (= `MessageTtlMs × 2`, default), treat the request
as orphaned:

1. Surface a "Request lost — please retry" error to the user.
2. Submit a **new `requestId`** — never reuse the original after an orphan.
3. Do NOT retry the original `requestId`; the idempotency key has expired and a duplicate
   execution may have already occurred.

**Server rule** (Phase 7 Response Dispatcher responsibility): `GET /result` MUST return a
`{ "status": "orphaned" }` discriminator (HTTP 404 body) when it can detect the orphan
condition (e.g., by checking the idempotency store — key absent + request age > threshold).
This lets clients distinguish "result not yet stored" (202) from "request genuinely lost" (404
orphaned) without relying solely on the client-side age heuristic.

### 3.5 POST /api/v1/requests/{id}/cancel — HTTP cancel

```http
POST /api/v1/requests/{id}/cancel
Authorization: Bearer <jwt>
```

Returns `202 Accepted`. See also §6.3 for the Invoke path (`hub.invoke("CancelRequest", id)`).

### 3.6 Event ingestion endpoints

```http
POST /api/v1/ingest/events         # single event
POST /api/v1/ingest/events/batch   # up to 500 events
Authorization: Bearer <api-key>    # separate API key auth (not user JWT)
```

### 3.7 How to choose between HTTP and Invoke

Both paths are fully supported. Mix and match per-request. Use these heuristics:

| Use case | Recommended path |
|---|---|
| App-load lookups before SignalR is connected | **HTTP** |
| Standard user interactions after SignalR connected | **Either** — Invoke is ~10-20ms faster (one fewer round-trip) |
| High-frequency interactions (rapid filter changes, table pagination) | **Invoke** |
| Corporate proxy / WebSocket-unreliable networks | **HTTP** |
| Debugging with DevTools Network tab | **HTTP** (visible in Network tab; Invoke is not) |
| Background data refresh / cron-like polling | **Either** |

---

## 4. Request Envelope

### 4.1 Full JSON schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["requestId", "operation", "params", "tenantId", "userId"],
  "properties": {
    "requestId":     { "type": "string", "description": "UUID v7" },
    "operation":     { "type": "string", "pattern": "^[a-z][a-z0-9]*(\\.[a-z][a-z0-9*]*)+$" },
    "params":        { "type": "object" },
    "tenantId":      { "type": "string" },
    "userId":        { "type": "string" },
    "correlationId": { "type": "string", "nullable": true },
    "options": {
      "type": "object",
      "properties": {
        "progress":     { "type": "boolean", "default": false },
        "cacheSeconds": { "type": "integer", "minimum": 0, "nullable": true },
        "priority":     { "type": "string", "enum": ["low", "normal", "high"], "default": "normal" },
        "timeoutMs":    { "type": "integer", "minimum": 100, "nullable": true }
      }
    }
  }
}
```

### 4.2 Field reference

| Field | Required | Description |
|---|---|---|
| `requestId` | Yes | UUID v7. Client-generated. Idempotency key. |
| `operation` | Yes | Dot-notation string, e.g. `"dashboard.render"` |
| `params` | Yes | Operation-specific parameters, validated against the registered JSON Schema |
| `tenantId` | Yes | Must match `tenant` claim in the JWT |
| `userId` | Yes | Must match `sub` claim in the JWT |
| `correlationId` | No | UUID for cross-request grouping (e.g., a single user session) |
| `options.progress` | No | `true` = SSE progress stream available; `false` (default) |
| `options.cacheSeconds` | No | Override widget cache TTL; `0` = bypass cache |
| `options.priority` | No | `"low"` \| `"normal"` (default) \| `"high"` |
| `options.timeoutMs` | No | Client-requested timeout; capped by registry's per-operation limit |

### 4.3 X-Connection-Id header (HTTP path only)

- **HTTP path**: set `X-Connection-Id: <hubConnection.connectionId>`. This tells the server which SignalR connection to push the response to.
- **Invoke path**: not needed. The Hub uses `Context.ConnectionId` of the calling connection.
- **HTTP path without header**: server still processes the request. Response is delivered via user-level push (all connections for that userId) when one appears, or via `GET /result`.

### 4.4 Idempotency guarantee

Same `requestId` → same terminal result, regardless of:
- Which submission transport was used (HTTP or Invoke)
- How many times you submit (only first accepted; subsequent return `DUPLICATE_REQUEST`)
- Whether the request has already completed (GET /result returns the cached terminal)

---

## 5. Response Envelopes

### 5.1 ResponseDispatchMessage (terminal)

```json
{
  "requestId": "01HQ7XXXXXXXXXXXXX",
  "status": "done",
  "operation": "dashboard.render",
  "payload": { },
  "error": null,
  "elapsedMs": 142,
  "tenantId": "tenant-001"
}
```

`status`: `"done"` | `"failed"` | `"timeout"` | `"cancelled"`

### 5.2 ResponseProgressMessage

```json
{
  "requestId": "01HQ7XXXXXXXXXXXXX",
  "percent": 42,
  "message": "Processing region North...",
  "tsUnixMs": 1716030042000
}
```

### 5.3 Error object

```json
{
  "code": "VALIDATION_ERROR",
  "message": "Field 'params.year' must be a number",
  "detailsJson": "{\"field\":\"params.year\",\"received\":\"string\"}",
  "retryable": false
}
```

### 5.4 Standard error codes

| Code | Meaning | Retryable |
|---|---|---|
| `OPERATION_NOT_FOUND` | Unknown operation string | No |
| `PROVIDER_UNAVAILABLE` | External provider circuit open | Yes (after delay) |
| `PROVIDER_DISCONNECTED` | Provider dropped mid-request | Yes |
| `PROVIDER_SUSPENDED` | Provider administratively suspended | No |
| `TIMEOUT` | Operation exceeded timeout | Maybe |
| `BACKPRESSURE` | Queue depth exceeded; system is shedding load | Yes (obey retryAfterMs) |
| `CANCELLED` | Client cancelled the request | No |
| `VALIDATION_ERROR` | params failed JSON Schema validation | No |
| `PARAMS_TOO_LARGE` | params payload exceeds 64 KB limit | No |
| `DUPLICATE_REQUEST` | requestId already submitted (idempotency) | No — check GET /result |
| `RATE_LIMITED` | Too many requests | Yes (obey Retry-After) |
| `UNAUTHORIZED` | Missing or invalid user JWT | No |
| `FORBIDDEN` | Insufficient role for this operation | No |
| `INTERNAL_ERROR` | Unexpected platform error | Yes |

> **Note on `CANCELLED`**: indicates user-initiated cancellation. The original `requestId` is consumed — to retry the operation, generate a new `requestId`.

---

## 6. SignalR Transport (`/hubs/main`)

### 6.1 Endpoint and protocol

- Endpoint: `/hubs/main`
- Protocol: **MessagePack** (binary) — required. JSON fallback not supported.
- Library: `@microsoft/signalr` + `@microsoft/signalr-protocol-msgpack`

```js
const connection = new HubConnectionBuilder()
  .withUrl("/hubs/main", {
    accessTokenFactory: () => authStore.getAccessToken()
  })
  .withHubProtocol(new MessagePackHubProtocol())
  .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000, 60000])
  .build();
await connection.start();
const connectionId = connection.connectionId; // store this for X-Connection-Id
```

### 6.2 Connection lifecycle

1. `connection.start()` → WebSocket established → JWT validated
2. Store `connection.connectionId` → use as `X-Connection-Id` on HTTP requests
3. Subscribe to push methods (§6.4) BEFORE submitting any requests
4. On disconnect: `withAutomaticReconnect` handles reconnection
5. On reconnect: `onreconnected` callback fires → fetch in-flight results via `GET /result`

### 6.3 Client → Server methods (invoke)

These are **optional** alternatives to HTTP endpoints. Use them for lower latency when the connection is already established.

#### `SubmitRequest(envelope: RequestEnvelope) → SubmitAck`

Alternative to `POST /api/v1/requests`. Identical semantics.

```js
const ack = await connection.invoke("SubmitRequest", envelope);
// ack: { requestId: string, queuedAt: string, progressStreamUrl: string | null }
```

Errors thrown as `HubException`:

| `err.message` | Equivalent to HTTP | `err.data` |
|---|---|---|
| `"VALIDATION_FAILED"` | `400` | `{ errors: [...] }` |
| `"UNAUTHORIZED"` | `401` | — |
| `"FORBIDDEN"` | `403` | — |
| `"DUPLICATE_REQUEST"` | `409` | — |
| `"BACKPRESSURE"` | `503` | `{ retryAfterMs: number }` |
| `"RATE_LIMITED"` | `429` | `{ retryAfterMs: number }` |

#### `CancelRequest(requestId: string) → void`

Alternative to `POST /api/v1/requests/{id}/cancel`.

```js
await connection.invoke("CancelRequest", "01HQ7XXXXXXXXXXXXX");
```

#### `SubscribeWidget(channel: string) → void`

Join the `WidgetStale` group for a specific widget. `channel` format: `"widget:{dashboardCode}:{widgetId}"`.

```js
await connection.invoke("SubscribeWidget", "widget:sales_2025:revenue_chart");
```

Server validates tenant access before joining. After joining, `WidgetStale` events for this widget are pushed to this connection.

#### `UnsubscribeWidget(channel: string) → void`

Leave a widget group.

### 6.4 Server → Client push methods (listen only)

Register handlers **before** starting the connection or before submitting requests.

```js
connection.on("RequestCompleted", (msg) => {
  // msg: ResponseDispatchMessage, status = "done"
  renderResult(msg.requestId, msg.payload);
});

connection.on("RequestFailed", (msg) => {
  // msg: ResponseDispatchMessage, status = "failed" | "timeout"
  showError(msg.requestId, msg.error);
});

connection.on("RequestCancelled", (msg) => {
  // msg: ResponseDispatchMessage, status = "cancelled"
  handleCancelled(msg.requestId);
});

connection.on("WidgetStale", (channel, hint) => {
  // channel: "widget:sales_2025:revenue_chart"
  // hint: { reason: "data_updated", updatedAt: "..." }
  refreshWidget(channel);
});
```

### 6.5 SubmitAck shape

```json
{
  "requestId": "01HQ7XXXXXXXXXXXXX",
  "queuedAt": "2026-05-18T10:00:00.000Z",
  "progressStreamUrl": "/sse/requests/01HQ7XXXXXXXXXXXXX/progress"
}
```

`progressStreamUrl` is non-null only when `options.progress: true`.

#### `SubmitAck` TypeScript type

```ts
interface SubmitAck {
  requestId: string;               // echoes the submitted requestId
  queuedAt: string;                // ISO 8601 UTC
  progressStreamUrl: string | null; // non-null only when options.progress was true
}
```

**Nullability rule**:
- `options.progress: true` → `progressStreamUrl: "/sse/requests/{requestId}/progress"`
- `options.progress: false` (or absent) → `progressStreamUrl: null`

This applies identically to the HTTP `202 Accepted` body and the Invoke return value.

### 6.6 Hub errors

`HubException` objects have:
- `message`: error code string (e.g. `"VALIDATION_FAILED"`)
- `data`: typed object with additional context (see §6.3 table)

**Frontend implementation note**: maintain a local set of in-flight `requestId`s per tab. Push events for unknown `requestId`s should be silently ignored — this happens naturally in multi-tab scenarios after a user-level fan-out (see `docs/DECISIONS.md` — Multi-tab user-level fan-out).

### 6.7 Response routing

1. If `connectionId` is known: push to that specific connection
2. If connection is gone: push to all connections for `userId` (user-level fan-out)
3. If no connections: result stored in Redis (GET /result works)

The routing is agnostic to which submission path was used.

### 6.8 Redis backplane

Transparent to frontend. Allows multiple server nodes to push to any client connection. No client-side configuration needed.

---

## 7. SSE Transport (Progress Streams)

### 7.1 Endpoint

`GET /sse/requests/{requestId}/progress`

Authentication:
- From a browser: `?access_token=<jwt>` query param (mandatory — `EventSource` API cannot set custom headers)
- From a non-browser client: `Authorization: Bearer <jwt>` header preferred; `?access_token=` accepted as fallback

### 7.2 When to use

Only useful when `options.progress: true` in the `RequestEnvelope`. If progress is `false`, no events are sent and the stream returns 404.

### 7.3 Open SSE BEFORE submitting (either transport)

The client must generate `requestId` itself (UUID v7) — this enables opening SSE with the requestId before submission.

```js
// CORRECT order:
//   1. Generate requestId locally
const requestId = generateUuidV7();

//   2. Open SSE with that requestId
const source = new EventSource(
  `/sse/requests/${requestId}/progress?access_token=${jwt}`
);
source.addEventListener("progress", (e) => updateProgressBar(JSON.parse(e.data)));
source.addEventListener("terminal", () => source.close());

//   3. (Optional) Wait for the SSE 'open' event if you want zero progress loss
await new Promise((resolve) => { source.onopen = resolve; });

//   4. Submit the request (either HTTP or Invoke) with the same requestId
const ack = await connection.invoke("SubmitRequest", {
  requestId,
  operation: "datasource.preview",
  params: { ... },
  options: { progress: true }
});
```

**Why this order matters**: if you submit first, and the operation completes in <50ms (cache hit, lookup operation), the SSE stream you open afterwards will receive **zero progress events** and will close on the terminal signal. Step 3 (awaiting `open`) eliminates even the small race between SSE open initiation and server-side handler registration.

**Server-side guarantee**: the backend buffers up to 100 progress events per requestId for up to 30 seconds before any SSE client connects. This covers the typical race window.

### 7.4 Event format

```
event: progress
data: {"requestId":"...","percent":42,"message":"Processing region North...","tsUnixMs":...}

event: terminal
data: {"requestId":"...","status":"done"}

```

The `terminal` event is a signal to close the SSE stream. The actual terminal payload arrives via SignalR (`RequestCompleted`/`RequestFailed`). Never use the `terminal` SSE event as the source of truth for the result.

### 7.5 Stream lifecycle

- Closes automatically after `terminal` event
- Closes after 5 minutes idle (no progress events)
- Frontend should close on component unmount: `source.close()`

---

## 8. Operation Catalogue

### 8.1 How to read this table

`Progress` = whether the operation streams SSE progress when `options.progress: true`. `Typical latency` = p95 observed end-to-end from submit to terminal push.

### 8.2 Built-in (internal) operations

| Operation | Params | Result payload | Progress | Latency |
|---|---|---|---|---|
| `dashboard.list` | `{ tenantId }` | `DashboardListPayload` | No | < 200ms |
| `dashboard.get` | `{ dashboardId }` | `DashboardPayload` | No | < 200ms |
| `dashboard.render` | `{ dashboardCode, filters?, dateRange? }` | `DashboardRenderPayload` | Optional | < 500ms |
| `widget.render` | `{ dashboardCode, widgetId, filters, _table? }` | `WidgetRenderPayload` | No | < 300ms |
| `widget.filterOptions` | `{ dashboardCode, widgetId, search? }` | option list | No | < 100ms |
| `widget.tableExport` | `{ dashboardCode, widgetId, filters, format }` | `{ downloadUrl }` | Optional | varies |
| `widget.drillContext` | `{ sourceDashboard, widgetId, clickedData, targetDashboard }` | `{ resolvedFilters }` | No | < 50ms |
| `datasource.list` | `{ tenantId }` | `DatasourceListPayload` | No | < 200ms |
| `datasource.preview` | `{ datasourceId, limit? }` | `DatasourcePreviewPayload` | No | < 2s |
| `metadata.dashboards.upsert` | `DashboardDefinition` | `{ id, version }` | No | < 100ms |
| `metadata.dashboards.delete` | `{ id }` | `{ deleted: true }` | No | < 100ms |
| `metadata.datasources.upsert` | `DatasourceDefinition` | `{ id, version }` | No | < 100ms |
| `metadata.schemas.upsert` | `SchemaDefinition` | `{ id, version }` | No | < 100ms |

### 8.3 Widget operations detail

- **`widget.render`**: send `_table: { page, pageSize, sort, filters }` inside `params.filters` for server-side pagination (advanced_table only). Each pagination change = a new `widget.render` request. Use Invoke for speed.
- **`widget.filterOptions`**: call on dashboard load for `filter_dropdown` widgets with `optionsSource.type == "datasource"`. Re-call on user search input (debounce 300ms).
- **`widget.tableExport`**: `format` = `"csv"` or `"xlsx"`. Small datasets: inline base64 in payload. Large: `downloadUrl` in payload + optional progress via SSE.
- **`widget.drillContext`**: call before navigating to target dashboard to resolve `{{clicked.*}}` template tokens. Returns `{ resolvedFilters, valid: true }`. Use resolved filters in the next `dashboard.render`.

### 8.4 External operations (transparent)

Same `RequestEnvelope`, same `ResponseDispatchMessage`. Higher latency possible. Client cannot distinguish internal from external — the platform handles routing.

### 8.5 Chart type switching (client-side only)

Compatible groups have identical data shapes. Frontend re-renders without calling the backend:
- `["line_chart", "bar_chart", "area_chart"]` — time-series/category
- `["pie_chart", "donut_chart"]` — part-of-whole
- `["simple_table", "advanced_table"]` — tabular (within size limits)

Switching across groups requires a new `widget.render` call.

### 8.6 Discovery

`GET /api/v1/admin/operations` returns the full operation registry with params/payload schemas.

### 8.7 Admin utility endpoints (stub)

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/admin/operations` | Full operation registry |
| `POST /api/v1/admin/providers/{id}/probe` | Connectivity probe — performs Hello/Welcome handshake using a synthetic JWT; returns `{ tlsHandshake, jwtAccepted, welcomeReceived, latencyMs }` |

The probe endpoint is used by monitoring and by admins onboarding new providers. See `docs/PROVIDER_ONBOARDING.md` §5.2 for usage. **Phase 2 work item**: implement as part of admin endpoint suite.

---

## 9. Reconnection Protocol

### 9.1 Works identically regardless of submission transport

Whether you submitted via HTTP or Invoke, the reconnection flow is the same. The result is in Redis result-store; the submission transport is irrelevant after the request is queued.

### 9.2 Step-by-step reconnection flow

1. Detect disconnection (SignalR `onclose` fires or HTTP call fails)
2. Re-establish SignalR connection (automatic if `withAutomaticReconnect` configured)
3. For each in-flight `requestId`: `GET /api/v1/requests/{id}/result`
   - `200 OK` → result is ready → render
   - `202 Accepted` → still in flight → wait for push on new connection
   - `404 Not Found` → TTL expired (> 5 min) → re-submit with same `requestId`
4. On re-submit: same `requestId` → server may return cached result immediately

### 9.3 Result TTL: 5 minutes from terminal event

### 9.4 Re-submit idempotency

Re-submitting with the same `requestId` is safe. If the operation completed, the terminal result is returned immediately. If still in flight, the ack is returned and you wait again.

---

## 10. Cancellation

### 10.1 Via HTTP

```http
POST /api/v1/requests/{requestId}/cancel
Authorization: Bearer <jwt>
```

Returns `202 Accepted`.

### 10.2 Via SignalR (Invoke)

```js
await connection.invoke("CancelRequest", requestId);
```

Lower latency; preferred when SignalR is already connected.

### 10.3 Cancel behavior and race conditions

Cancel is **best-effort**. The operation may have already completed by the time the cancel signal reaches the worker.

**Possible outcomes (single requestId, after Cancel is sent)**:

| Worker state at cancel time | Client receives |
|---|---|
| Not yet consumed from queue | `RequestCancelled` only |
| Consumed, processing not started | `RequestCancelled` only |
| Processing in progress, checks cancel | `RequestCancelled` only |
| Processing completed, terminal not published | `RequestCancelled` only |
| Terminal published, not yet pushed | `RequestCompleted` only (race lost — cancel ignored) |
| Terminal already pushed | `RequestCompleted` only (cancel is a no-op) |

**The client will NEVER receive both `RequestCompleted` and `RequestCancelled` for the same requestId.** The backend serializes the terminal write — whichever wins, wins.

**Frontend handling**:
- Treat `RequestCancelled` as "request is fully terminated" — discard UI state
- Treat `RequestCompleted` after a cancel as "cancel arrived too late, here is your result"
- Do NOT re-use the `requestId` after either terminal arrives

**Cancel itself never produces a `RequestFailed`** — even if the cancel call fails internally, the request continues normally.

### 10.4 External provider cancel propagation

For operations served by external providers: cancel signal is forwarded to the provider (best-effort). Bridge enforces a hard timeout regardless.

---

## 11. Progress Streaming — Client Checklist

- [ ] Set `options.progress: true` in `RequestEnvelope`
- [ ] **Generate `requestId` (UUID v7) locally before opening SSE**
- [ ] Open `GET /sse/requests/{requestId}/progress?access_token=<jwt>` **before** submitting (either transport)
- [ ] Listen for `progress` events: update UI with `percent` (range 1–99)
- [ ] Listen for `terminal` SSE event: close the SSE stream
- [ ] **Render terminal result from SignalR** (`RequestCompleted`/`RequestFailed`), NOT from SSE
- [ ] Close SSE on component unmount: `eventSource.close()`
- [ ] If SSE disconnects mid-stream: progress events are lost, but terminal still arrives via SignalR

---

## 12. Rate Limiting

| Limit | Value | Response |
|---|---|---|
| Per-user requests | 100/minute | HTTP 429 or HubException `"RATE_LIMITED"` |
| Per-tenant requests | 500/minute | HTTP 429 or HubException `"RATE_LIMITED"` |
| Ingest events | 2,000/minute per tenant | HTTP 429 |
| `BACKPRESSURE` (queue full) | Configurable | HTTP 503 or HubException `"BACKPRESSURE"` |
| SignalR connections | 5,000 concurrent (global) | Connection refused |

`Retry-After` header (in seconds) is set on 429 and 503 HTTP responses. For Invoke errors, `HubException.data.retryAfterMs` (in milliseconds) is set on both `RATE_LIMITED` and `BACKPRESSURE`. Frontend should normalize to one unit if mixing transports.

---

## 13. Request Validation

### 13.1 Identical for both submission paths

Same validation logic, same error codes, same field-level details. If you see a validation error via HTTP, the same request via Invoke produces the same error (as a HubException).

### 13.2 Error delivery

| Transport | Validation error delivery |
|---|---|
| HTTP | `400 Bad Request` — body: `{ errors: [...] }` |
| Invoke | `HubException` with `message = "VALIDATION_FAILED"`, `data.errors = [...]` |

### 13.3 Validation rules

- `params` validated against registered JSON Schema for the operation
- Unknown `operation`: immediate `OPERATION_NOT_FOUND` (does not enter queue)
- `params` > 64 KB: immediate rejection with error code `PARAMS_TOO_LARGE` (checked before schema validation)
- `tenantId` must match JWT `tenant` claim

---

## 14. OpenTelemetry / Distributed Tracing

- Send `traceparent` header (W3C TraceContext format) on all HTTP requests
- `traceparent` is not applicable to Invoke calls directly, but include `correlationId` in the envelope for cross-request grouping
- Find full request traces in Jaeger by searching `requestId` as a span attribute
- Frontend should create root spans for user interactions and propagate `traceparent` on the resulting HTTP request

---

## 15. Changelog

- **v6.3 (2026-05-18)**: **Dual request-submission transport**. Added `hub.invoke("SubmitRequest")` as an alternative to `POST /api/v1/requests`. Added `hub.invoke("CancelRequest")`, `SubscribeWidget`, `UnsubscribeWidget`. Updated §3 (full transport comparison + heuristics). Updated §4.3 (X-Connection-Id now HTTP-only). Updated §6 (SignalR now has client→server methods). Updated §9–11 to be transport-agnostic. Added `DUPLICATE_REQUEST` error code.
- **v6.2 (2026-05-18)**: Added `widget.filterOptions`, `widget.tableExport`, `widget.drillContext` operations. Added `_table` server-pagination protocol. Added chart type switching groups. Widget catalog locked to 16 types.
- **v6.1 (2026-05-18)**: External provider operations transparent to clients. Added provider error codes.
- **v5.1**: Initial async request-reply pattern, dual response transport (SignalR + SSE).
