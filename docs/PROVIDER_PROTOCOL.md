# PROVIDER_PROTOCOL.md — External Provider Integration Bible
> Version: 6.2 | Audience: External provider teams (any language) | Last updated: 2026-05-27

This document is the **single source of truth** for teams building external providers for the Realtime Reporting Platform. You do not need access to the platform's source code, RabbitMQ, Redis, or PostgreSQL. Everything you need is:
- This document
- `proto/provider.proto` (gRPC contract)
- A running `docker compose up` (dev environment)
- REST API access to register your operations

---

## Table of Contents

1. [Overview](#1-overview)
   - 1.1 What is a provider?
   - 1.2 Trust boundary — what providers CAN and CANNOT access
   - 1.3 Provider vs internal worker: key differences
   - 1.4 Supported languages (any language with gRPC + HTTP/1.1 support)
   - 1.5 What you deliver vs what the platform delivers

2. [Provider Lifecycle](#2-provider-lifecycle)
   - 2.1 Full lifecycle phase diagram
     ```
     register → issue credentials → obtain JWT → connect → serve
       → reconnect (on drop) → rotate credentials → suspend → decommission
     ```
   - 2.2 Provider state machine
     - `active` — connected and serving
     - `suspended` — administratively paused; no credential change
     - `disabled` — permanently off; operations fail
     - `credentials_revoked` — emergency stop; must rotate to restore
   - 2.3 Admin-managed vs self-service registration (controlled by platform policy)

3. [Authentication — OAuth2 Client Credentials Flow](#3-authentication)
   - 3.1 Credential types
     - `clientId` — public identifier (e.g. `ml-team-fraud-c83hf`); not a secret
     - `clientSecret` — secret; shown **ONCE** at registration; store in Vault immediately
     - Lost secret → rotate; no recovery path
   - 3.2 Token endpoint
     - `POST /api/v1/providers/token`
     - Request body:
       ```json
       {
         "clientId": "ml-team-fraud-c83hf",
         "clientSecret": "...",
         "grantType": "client_credentials"
       }
       ```
     - Success response (`200 OK`):
       ```json
       {
         "accessToken": "<jwt>",
         "expiresIn": 900,
         "tokenType": "Bearer"
       }
       ```
     - Error response (`401 Unauthorized`):
       ```json
       { "error": "invalid_client" }
       ```
       **Same error body for unknown `clientId` AND wrong `clientSecret`** — by design (no oracle attack)
   - 3.3 JWT claim reference
     ```json
     {
       "iss": "https://platform.example.com",
       "sub": "ml-team-fraud",
       "aud": "provider-bridge",
       "scope": "provider",
       "iat": 1716030000,
       "exp": 1716030900,
       "jti": "01HQ7XXXXXXXXXXXX"
     }
     ```
     - `sub` = your `providerId` — Bridge enforces `Hello.providerId == jwt.sub`
     - `aud` MUST equal `"provider-bridge"` — token rejected on any other endpoint
     - `scope` MUST contain `"provider"` — distinguishes from user tokens
     - `jti` — logged for audit trail
   - 3.4 JWKS endpoint (for offline JWT verification)
     - `GET /.well-known/jwks.json`
     - RSA-2048 public keys in standard JWKS format
     - `kid` header in JWT identifies the signing key
   - 3.5 Rate limits on token endpoint
     - 10 token requests/minute per `clientId`
     - 100 token requests/minute per source IP
     - 5 consecutive failures within 1 minute → `clientId` locked for 5 minutes
       - During lockout: even correct credentials return `401`
       - Lockout lifted automatically after 5 minutes
   - 3.6 Token lifetime and rotation requirement
     - Token valid for **900 seconds (15 minutes)**
     - SDK: auto-refreshes at 80% of `expiresIn` (720s after issuance)
     - Non-SDK: you MUST re-call the token endpoint before expiry (see §16.2)
     - Bridge sends `RefreshAuthRequired` hint at `jwt.exp - 60s` — reconnect immediately
   - 3.7 Credential rotation (admin-initiated, planned)
     - Admin calls `POST /admin/providers/{id}/credentials/rotate`
     - New `clientSecret` returned once to admin → securely delivered to your team
     - Old secret still valid for **60-second grace period**
     - Coordinate with your ops team: update config → restart provider within 60s
   - 3.8 Credential revocation (emergency)
     - Admin calls `POST /admin/providers/{id}/credentials/revoke`
     - All active gRPC streams for the provider close within 5 seconds (via Redis pub/sub)
     - All in-flight requests fail with `PROVIDER_DISCONNECTED` (visible to clients)
     - New token requests fail immediately with `401 invalid_client`
     - Resolution: admin rotates credentials → new secret delivered → restart provider

     **What the provider observes on its stream**:

     The gRPC stream terminates with status `CANCELLED` and the trailer metadata key `x-platform-disconnect-reason` set to one of:
     - `credentials_revoked` — emergency revocation
     - `provider_suspended` — administrative pause (see §2.2)
     - `server_shutdown` — Bridge maintenance / rolling deploy
     - `idle_timeout` — heartbeat missed for 30s

     **Recommended provider behaviour by reason**:

     | Reason | Action |
     |---|---|
     | `credentials_revoked` | STOP reconnection attempts. Alert ops team. Wait for new credentials from admin. |
     | `provider_suspended` | Pause reconnection. Retry every 60s checking provider status via `/admin/providers/{id}/health` (if exposed to your team). |
     | `server_shutdown` | Reconnect with exponential backoff (1s, 2s, 4s, 8s, max 30s). Expected during platform rolling deploys. |
     | `idle_timeout` | Reconnect immediately. Investigate why heartbeats were missed (network, GC pause, etc). |

     SDK handles all of these automatically. Non-SDK providers must inspect trailer metadata and apply the recommended behaviour.
   - 3.9 Security checklist
     - Never log `clientSecret` or `accessToken`
     - Store `clientSecret` in Vault / Kubernetes secret / AWS Secrets Manager
     - Never put credentials in Docker images or environment files in version control
     - TLS validation MUST be enabled (do not skip cert verification)

4. [Connection Establishment](#4-connection-establishment)
   - 4.1 gRPC channel configuration
     - Endpoint: `grpcs://provider-bridge.platform:443`
     - TLS: **server certificate only** — standard TLS, no client certificate required
     - Validate server cert against platform CA chain (download from PROVIDER_ONBOARDING.md §A)
     - Keep-alive: 30s ping, 5s timeout
   - 4.2 Attaching JWT to gRPC metadata
     - Key: `authorization`
     - Value: `Bearer <accessToken>` (note space between "Bearer" and token)
   - 4.3 `Hello` message (must be first message on stream, within 5 seconds)
     ```json
     {
       "hello": {
         "providerId": "ml-team-fraud",
         "version": "1.0.0",
         "supportedOperations": ["ml.fraud.score", "ml.fraud.batchScore"],
         "metadata": { "language": "dotnet", "instanceId": "pod-abc123" }
       }
     }
     ```
     - `providerId` MUST match `jwt.sub` — mismatch → stream rejected `UNAUTHENTICATED`
     - `supportedOperations` MUST be a subset of registered operations — otherwise `INVALID_ARGUMENT`
     - `version` in semver format (informational)
   - 4.4 `Welcome` message (sent by Bridge on success)
     - `sessionId` — UUID for this connection; log it for correlation
     - `maxConcurrentRequests` — hard limit; do not exceed
     - `heartbeatIntervalSeconds` — default 30
   - 4.5 Rejection scenarios and gRPC status codes

     | Scenario | gRPC status |
     |---|---|
     | No JWT in metadata | `UNAUTHENTICATED` |
     | Expired JWT | `UNAUTHENTICATED` |
     | Wrong `aud` in JWT | `UNAUTHENTICATED` |
     | Wrong `scope` in JWT | `UNAUTHENTICATED` |
     | JWT signed with unknown key | `UNAUTHENTICATED` |
     | `Hello.providerId` != `jwt.sub` | `UNAUTHENTICATED` |
     | Provider status = `credentials_revoked` | `UNAUTHENTICATED` |
     | Provider status ≠ `active` | `PERMISSION_DENIED` |
     | Hello not received within 5s | `DEADLINE_EXCEEDED` |
     | `supportedOperations` not subset of registered | `INVALID_ARGUMENT` |

   - 4.6 Heartbeat — send `Heartbeat { tsUnixMs }` every `heartbeatIntervalSeconds`
     - Bridge closes stream after 30s without heartbeat
     - Treat as a keep-alive ping
   - 4.7 `RefreshAuthRequired` hint from server
     - Bridge sends this at `jwt.exp - 60s`
     - `reason`: `"token_expiring_soon"` or `"key_rotation"`
     - Action: immediately re-fetch JWT from token endpoint → open a new stream with new JWT
     - Your old stream will be forcibly closed at `jwt.exp` if not refreshed
   - 4.8 Multiple instances (HA)
     - All instances use the same `providerId` and `clientId`
     - Each instance gets its own JWT (concurrent token fetches allowed, each counts against rate limit)
     - Bridge assigns unique `sessionId` to each instance
     - Requests distributed across instances via competing consumer (RabbitMQ)

5. [Serving Operations](#5-serving-operations)
   - 5.1 `OperationRequest` field reference

     | Field | Type | Description |
     |---|---|---|
     | `requestId` | string (UUID v7) | Idempotency key |
     | `operation` | string | e.g. `"ml.fraud.score"` |
     | `paramsJson` | string (JSON) | Validated against registered schema |
     | `tenantId` | string | Always propagate; do not serve cross-tenant data |
     | `userId` | string | Requesting user |
     | `timeoutAtUnixMs` | int64 | Hard deadline — abort processing after this |
     | `wantsProgress` | bool | If false, skip sending progress chunks |
     | `traceparent` | string | W3C trace context — MUST propagate |
     | `correlationId` | string | Cross-request grouping (may be empty) |

   - 5.2 Processing model
     - Process up to `Welcome.maxConcurrentRequests` simultaneously
     - Each request in its own goroutine / Task / thread
     - Back-pressure: Bridge uses RabbitMQ prefetch; do not try to exceed the limit
   - 5.3 Sending progress chunks (only when `wantsProgress = true`)
     ```proto
     OperationResponseChunk {
       requestId: "..."
       progress: Progress { percent: 42, message: "Scoring batch 2/5", tsUnixMs: ... }
     }
     ```
     - `percent`: 1–99 (NEVER send 100 — send `Terminal` instead)
     - Frequency: no more than once per second (platform throttles beyond this)
     - If `wantsProgress = false`: skip all progress chunks
   - 5.4 Sending terminal chunk (always required, for every request)
     ```proto
     OperationResponseChunk {
       requestId: "..."
       terminal: Terminal {
         status: DONE
         payloadJson: "{\"score\": 0.87, \"explanation\": \"...\"}"
         elapsedMs: 123
       }
     }
     ```
     - `status`: `DONE`, `FAILED`, or `CANCELLED`
     - `payloadJson`: MUST be valid JSON when `DONE`; MUST match registered `payloadSchema`
     - `error`: populate when `FAILED`
   - 5.5 Idempotency requirement
     - If you receive the same `(requestId, operation)` twice: return the same result
     - This happens on reconnect after disconnect (Bridge requeues unACKed messages)
     - Cache results keyed by `requestId` for at least 5 minutes if your operation has side effects

6. [Error Handling](#6-error-handling)
   - 6.1 Standard error codes for providers to use in `Terminal.error.code`

     | Code | When to use |
     |---|---|
     | `INTERNAL_ERROR` | Unexpected failure in your provider |
     | `VALIDATION_ERROR` | `paramsJson` passes schema but fails business validation |
     | `RESOURCE_UNAVAILABLE` | Dependency (DB, model) temporarily down |
     | `DEPENDENCY_FAILED` | Upstream API returned an error |
     | `RATE_LIMITED_UPSTREAM` | Your upstream throttled you |
     | `TIMEOUT` | You detected you exceeded your internal deadline |

   - 6.2 Error object format:
     ```proto
     Error {
       code: "RESOURCE_UNAVAILABLE"
       message: "ML model inference endpoint returned 503"
       detailsJson: "{\"endpoint\":\"...\",\"retryable\":true}"
     }
     ```
   - 6.3 Do NOT surface internal infrastructure details (connection strings, internal hostnames) in error messages
   - 6.4 Mark errors as retryable in `detailsJson` if idempotent; Bridge uses this for retry policy

7. [Cancellation](#7-cancellation)
   - 7.1 Bridge sends `Cancel { requestId }` when client cancels
   - 7.2 Your response: stop processing ASAP, send `Terminal { status: CANCELLED }`
   - 7.3 Cancellation is best-effort — if you already computed the result, send `DONE`
   - 7.4 If you receive Cancel for an unknown `requestId`: ignore it silently
   - 7.5 Bridge enforces hard timeout regardless of whether you honour Cancel

8. [Distributed Tracing (W3C TraceContext)](#8-distributed-tracing)
   - 8.1 Extract `traceparent` from `OperationRequest.traceparent`
   - 8.2 Propagate to ALL downstream calls: HTTP, gRPC, database queries, ML model inference
   - 8.3 Create child spans for each significant sub-operation
   - 8.4 Baggage: propagate `tenantId` as baggage if your observability stack supports it
   - 8.5 Verification: check Jaeger — a full request trace should be one span tree from client HTTP through your provider

9. [Operation Registration](#9-operation-registration)
   - 9.1 Required fields for `POST /api/v1/admin/operations`
     ```json
     {
       "operationPattern": "ml.fraud.score",
       "handlerType": "external",
       "providerId": "ml-team-fraud",
       "schemaVersion": "1.0.0",
       "paramsSchema": { "$schema": "...", "type": "object", "properties": { ... } },
       "payloadSchema": { "$schema": "...", "type": "object", "properties": { ... } },
       "timeoutMs": 1000,
       "cacheable": false,
       "idempotent": true
     }
     ```
   - 9.2 Wildcard patterns: `"ml.fraud.*"` matches `ml.fraud.score` and `ml.fraud.batchScore`
     - Trailing `.*` matches exactly one segment; trailing `.**` matches one or more segments (greedy).
     - Most specific match wins: `"ml.fraud.score"` beats `"ml.fraud.*"` beats `"ml.**"`
   - 9.3 Marking operations as cacheable: `cacheable: true` + `cacheSeconds` in `DatasourceDefinition`
   - 9.4 Marking operations as idempotent: `idempotent: true` — enables Bridge retry on transient failure
   - 9.5 JSON Schema examples (see `samples/` for full examples)
   - 9.6 Deprecation flow: `status = deprecated` → clients warned → `status = disabled` after 90 days

10. [Health & Metrics](#10-health--metrics)
    - 10.1 Prometheus endpoint: `GET /metrics` (expected by platform monitoring)
    - 10.2 Required metrics:

      | Metric | Labels | Type |
      |---|---|---|
      | `provider_operations_total` | `operation`, `status` | Counter |
      | `provider_operation_duration_seconds` | `operation` | Histogram |
      | `provider_grpc_reconnections_total` | — | Counter |
      | `provider_grpc_stream_active` | — | Gauge |

    - 10.3 Health endpoint: `GET /health` → `{ "status": "healthy"|"degraded"|"unhealthy" }`
    - 10.4 Platform-visible health: `GET /admin/providers/{id}/health` (Bridge aggregates)

11. [Versioning & Compatibility](#11-versioning--compatibility)
    - 11.1 `schemaVersion` in semver — bump minor for additive changes; bump major for breaking
    - 11.2 Breaking change = removed/renamed required field in `paramsSchema` or `payloadSchema`
    - 11.3 Deprecation timeline: `deprecated` → 90 days notice → `disabled`
    - 11.4 Multiple versions: register separate `operationPattern` per version (e.g. `ml.fraud.score.v2`)
    - 11.5 Proto field additions are backward compatible — never remove or reuse field numbers

12. [Resilience — Bridge Behaviour (for provider awareness)](#12-resilience)
    - 12.1 **Timeout**: Bridge sends `Cancel` + publishes `TIMEOUT` after `operation.timeoutMs`. Your session continues — next requests still arrive.
    - 12.2 **Retry**: Bridge retries only if `idempotent: true` in registry. Default: no retry.
    - 12.3 **Circuit breaker**: opens when `failureThreshold`% of requests fail in `samplingDuration`. While open, new requests return `PROVIDER_UNAVAILABLE` immediately — they never reach you.
    - 12.4 **Bulkhead**: Bridge enforces `maxConcurrentRequests` (from `Welcome`). Additional messages queue in RabbitMQ.
    - 12.5 **Requeue on disconnect**: unACKed RabbitMQ messages (not yet dispatched to you) are requeued. You will receive them on reconnect — hence idempotency requirement.
    - 12.6 **Multiple instances**: competing consumers on same queue; each message goes to exactly one instance.

13. [Reconnection Protocol](#13-reconnection-protocol)
    - 13.1 SDK: automatic exponential backoff — 1s, 2s, 4s, 8s, max 30s. The SDK suppresses reconnection attempts when the last stream close reason was `credentials_revoked` — alerting your application via an `OnCredentialsRevoked` callback instead.
    - 13.2 Non-SDK reconnection sequence:
      1. Re-fetch JWT from token endpoint (`POST /api/v1/providers/token`)
      2. Create new gRPC channel with new JWT
      3. Send `Hello` on new stream
      4. Await `Welcome`
      5. Resume serving
    - 13.3 In-flight requests during disconnect: unprocessed messages requeued; you will receive them again
    - 13.4 Processed-but-unsent results: if you computed a result but couldn't send it before disconnect, you may receive the same `requestId` again — your idempotency cache should handle this

14. [Sequence Diagrams](#14-sequence-diagrams)
    - 14.1 Simple request (no progress)
      ```
      Client            Platform            Bridge              Provider
        │─POST /requests──►│                    │                   │
        │                  │─publish q.router──►│                   │
        │                  │                    │─gRPC request─────►│
        │                  │                    │◄─Terminal(DONE)───│
        │                  │◄─publish responses.dispatch────────────│
        │◄─SignalR completed│                   │                   │
      ```
    - 14.2 Request with progress streaming
    - 14.3 Cancellation mid-request
    - 14.4 Provider disconnect mid-request + recovery
    - 14.5 JWT expiry during long-running operation (`RefreshAuthRequired` flow)
    - 14.6 Provider crash and circuit breaker opening → half-open → recovery
    - 14.7 Credential rotation with 60s grace period

15. [Security Checklist for Provider Teams](#15-security-checklist)
    - [ ] `clientSecret` stored in secret manager (not in env files in version control)
    - [ ] `accessToken` never logged
    - [ ] TLS validation enabled on gRPC channel (do NOT set `SslCredentials.Insecure`)
    - [ ] `traceparent` propagated to all downstream calls
    - [ ] `tenantId` from `OperationRequest` used as isolation key in ALL data queries
    - [ ] `requestId` used as idempotency key
    - [ ] No internal infrastructure details in error messages

16. [Polyglot Guide](#16-polyglot-guide)
17. [Provider Payload Contract for Dashboard Widgets](#17-provider-payload-contract-for-dashboard-widgets)
    - 16.1 Python reference implementation: `samples/PythonProviderSample/`
      - grpc library: `grpcio` + `grpcio-tools`
      - Proto generation: `python -m grpc_tools.protoc -I proto --python_out=. --grpc_python_out=. provider.proto`
    - 16.2 Manual JWT refresh pattern (non-SDK)
      ```python
      # At 80% of expiresIn (720s for 900s token):
      def should_refresh(token_issued_at, expires_in):
          return time.time() > token_issued_at + expires_in * 0.8

      # Reconnection loop:
      while True:
          token = fetch_token(client_id, client_secret)
          channel = grpc.secure_channel(bridge_endpoint, creds_with_jwt(token))
          stream = stub.Connect()
          stream.send(Hello(...))
          welcome = stream.recv()  # Welcome
          try:
              serve_requests(stream, token)
          except RefreshRequired:
              channel.close()
              continue  # re-fetch token, reconnect
      ```
    - 16.3 Go reference: generate from proto using `protoc-gen-go-grpc`
    - 16.4 Rust reference: `tonic` crate + `prost`
    - 16.5 Testing without full platform: use `grpc-mock` or in-repo Bridge stub (see `tests/ProviderBridge.Tests/`)

17. [Provider Payload Contract for Dashboard Widgets](#17-provider-payload-contract-for-dashboard-widgets)
    - 17.1 When is this contract used?
    - 17.2 Payload JSON structure
    - 17.3 Column schema (optional)
    - 17.4 Failure case
    - 17.5 Datasource definition example
18. [SSE Progress Event Ordering — Late Arrival Contract](#18-sse-progress-event-ordering--late-arrival-contract)
19. [Widget Render Contract for Dashboard Operations](#19-widget-render-contract-for-dashboard-operations)
    - 19.1 `resultChartType` field
    - 19.2 Full widget type table (31 types)
    - 19.3 How to choose a chart type for your operation
    - 19.4 Validate endpoint

---

## 17. Provider Payload Contract for Dashboard Widgets

This section applies when a dashboard datasource has `type: "external_provider"`. The platform's `ExternalProviderAdapter` calls your operation and maps the `payloadJson` from your `Terminal` response into widget rows. Your payload MUST follow the contract described here.

### 17.1 When is this contract used?

Every time a widget backed by an `external_provider` datasource is rendered:

```
Dashboard render → ExternalProviderAdapter → your operation → Terminal.payloadJson → widget rows
```

The `payloadJson` field in your `Terminal` message is the sole data source for the widget.

### 17.2 Payload JSON structure

```json
{
  "rows": [
    { "columnName1": "value", "columnName2": 42.5 },
    { "columnName1": "value2", "columnName2": 17.0 }
  ],
  "totalRows": 2,
  "schema": [
    { "name": "columnName1", "type": "string" },
    { "name": "columnName2", "type": "number" }
  ]
}
```

| Field | Required | Type | Notes |
|-------|----------|------|-------|
| `rows` | **Yes** (unless `rowsPath` set) | Array of objects | Each object is one row; keys are column names |
| `totalRows` | No | Integer | For paginated widgets; omit if not paginated |
| `schema` | No | Array of `{name, type}` | Column metadata; used by AdvancedTable widget for header rendering |

**Alternate `rowsPath`**: if the datasource `connectionConfig.rowsPath` is set (e.g. `"data"`), the adapter reads `payload["data"]` as the rows array instead of `payload["rows"]`. Use this when your provider's natural response structure uses a different key.

### 17.3 Column schema (optional)

```json
"schema": [
  { "name": "transactionId", "type": "string" },
  { "name": "score",          "type": "number" },
  { "name": "riskBand",       "type": "string" },
  { "name": "timestamp",      "type": "datetime" }
]
```

Recognized types: `string`, `number`, `integer`, `boolean`, `datetime`. Unknown types are passed through as-is — the widget transformer handles display.

### 17.4 Failure case

If your operation cannot compute a result, send `Terminal { status: FAILED }` with an `Error` object. The platform maps this to `AdapterException("PROVIDER_FAILED", errorCode)`, which renders the widget with an error state — no rows are shown.

Do NOT return an empty `rows: []` to signal failure; use `FAILED` status. An empty rows array is valid (zero-result query).

### 17.5 Datasource definition example

Stored in Postgres `datasources.connection_config` JSONB:

```json
{
  "operationName": "ml.fraud.score",
  "providerId":    "fraud-svc-v1",
  "paramMapping": {
    "transactionId": "{{filters.transaction_id}}",
    "amount":        "{{filters.amount}}"
  },
  "rowsPath":  null,
  "timeoutMs": 3000
}
```

The `paramMapping` maps widget filter values into provider params using `{{filters.key}}` tokens. Unknown filter keys resolve to JSON `null`. Literal string values (no `{{}}`) are passed through as-is.

---

## 18. SSE Progress Event Ordering — Late Arrival Contract

### 18.1 Terminal event is the authoritative end-of-stream signal

When the platform dispatches a `Terminal` gRPC message (`status: DONE`, `FAILED`, or `CANCELLED`) for a request, it publishes a terminal SSE event on the `rp:sse-terminal:{requestId}` channel. The frontend receives this as a `done`, `failed`, or `cancelled` event on the SSE stream.

### 18.2 Late progress events are acceptable

> **Clients SHOULD ignore progress events received after the terminal event for a given `requestId`.**

Due to the distributed pub/sub architecture, progress events emitted close to the terminal event may arrive on the SSE stream *after* the terminal event has been dispatched. This is an inherent property of asynchronous message passing and is not an error.

**When this happens**:
- Progress events are buffered in a Redis Stream ring buffer (up to 100 events, 30-second TTL).
- The SSE endpoint delivers buffered events in order, but the terminal event may arrive first if it was published on a higher-priority channel.
- For nested provider calls (dashboard widget renders), progress forwarding is best-effort — a forwarded progress event may arrive after the parent terminal.

**Client responsibility**:
1. On receiving a terminal event (`done`, `failed`, `cancelled`): mark the `requestId` as complete.
2. If a subsequent progress event arrives for a `requestId` already marked complete: silently discard it.
3. Do NOT retry or re-open the SSE stream based on a late progress event.

**Provider responsibility**: none — this is an infrastructure concern, not a provider concern. Providers emit progress via `OperationProgress` gRPC stream messages and emit the final `Terminal` message when computation is complete. The platform handles ordering best-effort.

### 18.3 Progress event format

Progress events arrive as SSE `data:` lines with JSON:
```json
{
  "requestId": "abc123",
  "percent": 75,
  "message": "Processing rows 750/1000..."
}
```

Terminal events:
```
event: done
data: {"requestId":"abc123","status":"completed"}
```

---

## 19. Widget Render Contract for Dashboard Operations

This section applies when your operation is wired into the **Dashboard Designer** — when a user places a widget on a dashboard backed by your operation, the platform calls your operation and maps `Terminal.payloadJson` into the widget renderer using the `chartType` contract you declared.

### 19.1 `resultChartType` field

When registering an operation that will power a dashboard widget, include `resultChartType` in the registration payload:

```json
{
  "operationPattern": "report.dashboard.summary",
  "handlerType": "external",
  "providerId": "excel-provider",
  "resultChartType": "kpi_grid",
  "payloadSchema": {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "type": "object",
    "required": ["rows"],
    "properties": {
      "rows": { "type": "array" }
    }
  },
  "timeoutMs": 5000,
  "cacheable": true,
  "idempotent": true
}
```

**Effect of `resultChartType`:**
- Platform validates your `payloadSchema` is compatible with the declared chart type's JSON Schema (from `widget_type_catalog`)
- Dashboard Designer surfaces this operation only in the "compatible operations" panel for that chart type
- Frontend knows which rendering contract to apply without probing the payload
- `GET /api/v1/admin/operations?resultChartType=kpi_grid` returns all operations you can wire to a `kpi_grid` widget

If `resultChartType` is omitted or `null`: operation is treated as `raw_json` — usable in widgets but rendered as pretty-printed JSON only.

### 19.2 Full widget type table (31 types)

| `resultChartType` | Category | Provider `payloadJson` shape |
|---|---|---|
| `line_chart` | Visualization | `{ rows: [{x, y, series?}] }` |
| `bar_chart` | Visualization | `{ rows: [{x, y, series?}] }` |
| `area_chart` | Visualization | `{ rows: [{x, y, series?}] }` |
| `pie_chart` | Visualization | `{ rows: [{label, value, color?}] }` |
| `donut_chart` | Visualization | `{ rows: [{label, value, color?}] }` |
| `kpi` | Visualization | `{ rows: [{value, label, deltaPercent?, direction?, sparkline?}] }` (single row) |
| `gauge` | Visualization | `{ rows: [{value, min, max, unit?}] }` (single row) |
| `heatmap` | Visualization | `{ rows: [{x, y, value}] }` |
| `scatter` | Visualization | `{ rows: [{x, y, size?, label?, series?}] }` |
| `advanced_table` | Visualization | `{ rows: [...], schema: [{name, type}], totalRows? }` |
| `simple_table` | Visualization | `{ rows: [...], schema: [{name, type}] }` |
| `pivot_table` | Visualization | `{ rows: [...], schema: [...], pivotConfig: {rowDims, colDims, measures} }` |
| `funnel` | Visualization | `{ rows: [{label, value}] }` (ordered, first row = top) |
| `kpi_grid` | Healthcare | `{ rows: [{id, label, value, format?, deltaPercent?, direction?, sparkline?, icon?, variant?}] }` |
| `progress_rows` | Healthcare | `{ rows: [{id, label, sublabel?, current, max, badge?, badgeVariant?}] }` |
| `flow_steps` | Healthcare | `{ rows: [{id, label, sublabel?, status, count?}] }` (ordered) |
| `timeline_vertical` | Healthcare | `{ rows: [{id, timeLabel, isoTime?, title, subtitle?, status, actor?, note?}] }` (ordered) |
| `alert_list` | Healthcare | `{ rows: [{id, level, title, subtitle?, time, acknowledged, runbookId?}] }` |
| `bed_grid` | Healthcare | `{ rows: [{deptId, deptName, bedId, bedLabel, status, patientId?, patientName?}] }` |
| `room_status_grid` | Healthcare | `{ rows: [{id, label, status, primaryText, secondaryText?, progressPercent?}] }` |
| `map_pins` | Healthcare | `{ rows: [{id, x, y, label, sublabel?, status, type?, metadata?}] }` |
| `patient_flow_stages` | Healthcare | `{ rows: [{id, label, count, avgWaitMin?, status?}] }` (ordered) |
| `risk_tiers` | Healthcare | `{ rows: [{level, label, count, percent, color, action?, changeFromPrev?}] }` |
| `news2_bars` | Healthcare | `{ rows: [{id, name, ward?, bed?, score, level, trend?, lastAssessed?}] }` |
| `chat_panel` | Healthcare/AI | Static config — no rows. See §19.3. |

> **Note on row-based contract**: The platform's `ExternalProviderAdapter` reads `payload.rows` (or `payload[rowsPath]` if configured). For chart types with a specific shape, the adapter transforms rows into the widget-specific contract (see `RENDER_CONTRACTS.md §3–11`). Your payload just needs to provide rows with the right column names.

### 19.3 How to choose a chart type for your operation

| I want to display... | Use `resultChartType` |
|---|---|
| A single number with trend | `kpi` |
| Multiple numbers side by side | `kpi_grid` |
| A line over time | `line_chart` or `area_chart` |
| Bars comparing categories | `bar_chart` |
| Part-of-whole breakdown | `pie_chart` or `donut_chart` |
| Bed / room occupancy progress | `progress_rows` |
| Patient care steps / pathway | `flow_steps` |
| Historical event timeline | `timeline_vertical` |
| Live critical alerts feed | `alert_list` |
| Hospital bed map grid | `bed_grid` |
| OR / ICU room status | `room_status_grid` |
| Ambulance / asset on floor plan | `map_pins` |
| ER patient flow counts | `patient_flow_stages` |
| Risk stratification by tier | `risk_tiers` |
| NEWS2 scores per patient | `news2_bars` |
| AI chatbot panel | `chat_panel` |
| A full data table (paginated) | `advanced_table` |
| A small data table | `simple_table` |
| Cross-tabulation | `pivot_table` |
| Conversion / dropout funnel | `funnel` |
| Something not in this list | omit `resultChartType` → `raw_json` |

### 19.4 Validate endpoint

Before submitting `resultChartType` in your operation registration, you can validate that your `payloadSchema` is compatible:

```http
POST /api/v1/admin/schemas/validate
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "chartType": "kpi_grid",
  "payloadSchema": {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "type": "object",
    "required": ["rows"],
    "properties": {
      "rows": {
        "type": "array",
        "items": {
          "type": "object",
          "required": ["id", "label", "value"],
          "properties": {
            "id":    { "type": "string" },
            "label": { "type": "string" },
            "value": { "type": "number" }
          }
        }
      }
    }
  }
}
```

Response:
```json
{
  "valid": true,
  "warnings": [],
  "requiredColumns": ["id", "label", "value"],
  "optionalColumns": ["format", "deltaPercent", "direction", "sparkline", "icon", "variant"]
}
```

If `valid: false`, `errors` contains the incompatible fields. Fix these before registering the operation.
