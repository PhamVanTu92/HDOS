# Ingestion API

HTTP API for ingesting external events into the platform. Accepts single-event and batch-event payloads from authenticated callers and publishes them to RabbitMQ for downstream processing.

**Port:** 5100 (internal — access via Gateway at port 5500, path `/api/v1/events`)

---

## Responsibilities

- `POST /api/v1/events` — ingest a single event.
- `POST /api/v1/events/batch` — ingest a batch of up to 1 000 events in one request.
- Validate event payloads against registered JSON schemas (per `tenant_id` + `eventType`).
- Enforce per-tenant ingestion rate limits (fixed-window, configurable).
- Publish validated events as `IngestEventEnvelope` messages to RabbitMQ.

## Limits

| Limit | Value | Error code |
|---|---|---|
| Max events per batch | 1 000 | `BATCH_TOO_LARGE` (400) |
| Per-tenant rate limit | 1 000 req/min (configurable) | `RATE_LIMIT_EXCEEDED` (429) |

## Key dependencies

| Dependency | Purpose |
|---|---|
| PostgreSQL | Schema registry (event type → JSON Schema) |
| Redis | Per-tenant rate limit state |
| RabbitMQ | Publish `IngestEventEnvelope` messages |

## Configuration

| Key | Default | Description |
|---|---|---|
| `Ingestion:RateLimits:Default` | `1000` | Per-tenant request limit per minute |
| `ConnectionStrings:Postgres` | _(required)_ | PostgreSQL connection string |
| `Redis:ConnectionString` | _(required)_ | Redis host:port |
| `RabbitMQ:Uri` | _(required)_ | RabbitMQ AMQP URI |
| `Auth:Authority` | _(required)_ | OIDC authority for JWT validation |

## Health check

`GET /health` → 200.

## Security notes

- Requires JWT with `scope=ingestion` claim. Requests without this scope receive 403.
- `tenant_id` is always read from the JWT — never from the event payload body.
- Schema validation is per `(tenant_id, eventType)` pair. Unknown event types pass through if no schema is registered for that tenant.
