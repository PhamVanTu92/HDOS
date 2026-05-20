# Request API

HTTP API for the dashboard request lifecycle. Handles request submission, status polling, result retrieval, cancellation, and provider/dashboard metadata management.

**Port:** 5000 (internal — access via Gateway at port 5500)

---

## Responsibilities

- Accept `POST /api/v1/requests` — validate, persist, and enqueue `OperationRequested` to RabbitMQ.
- `GET /api/v1/requests/{id}/result` — polling fallback for clients that missed the SignalR push.
- `POST /api/v1/requests/{id}/cancel` — mark a request cancelled; Operation Router stops processing.
- `GET /sse/requests/{sessionId}` — SSE stream for real-time progress updates (alternative to SignalR).
- Admin endpoints (`/api/v1/admin/**`) — provider registration, dashboard metadata management.
- `GET /.well-known/jwks.json` — serves the platform's public JWKS for Provider Bridge JWT validation.

## Key dependencies

| Dependency | Purpose |
|---|---|
| PostgreSQL | Persist requests, results, provider and dashboard metadata |
| Redis | Request status cache; SignalR backplane (shared with Realtime Hub) |
| RabbitMQ | Publish `OperationRequested`, consume `OperationCompleted` |

## Configuration

| Key | Description |
|---|---|
| `ConnectionStrings:Postgres` | PostgreSQL connection string |
| `Redis:ConnectionString` | Redis host:port |
| `RabbitMQ:Uri` | RabbitMQ AMQP URI |
| `Auth:Authority` | OIDC authority for JWT validation |
| `Auth:Audience` | Expected JWT audience |

## Health check

`GET /healthz/live` → 200. Checks that the ASP.NET Core host is running (does not probe dependencies).

## Security notes

- `tenant_id` is always extracted from the JWT `tenant_id` claim — never from request bodies.
- `clientSecret` is stored bcrypt-hashed (cost factor 12). Plaintext is returned only at provider registration time.
- JWKS endpoint is publicly accessible (no auth) — it serves only public keys.
