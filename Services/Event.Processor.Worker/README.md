# Event Processor Worker

Background worker that processes ingested external events, applies tenant-specific transformation logic, and delivers results to subscribers.

---

## Responsibilities

- Consume `IngestEventEnvelope` messages from RabbitMQ (published by Ingestion API).
- Look up active event subscriptions for the `(tenant_id, eventType)` pair in PostgreSQL.
- Apply the configured transformation pipeline to the event payload.
- For each active subscription, resolve the target dashboard and push a result via Realtime Hub.
- Persist processed event records for audit and replay.

## Message flow

```
RabbitMQ (IngestEventEnvelope)
  → Event Processor Worker
    → PostgreSQL (lookup event_subscriptions for tenant + eventType)
    → Transformation pipeline (payload → dashboard result format)
    → PostgreSQL (persist processed event)
    → Realtime Hub (push to subscriber connections)
```

## Key dependencies

| Dependency | Purpose |
|---|---|
| PostgreSQL | Event subscription registry; processed event persistence |
| Redis | Connection state for targeted push fallback |
| RabbitMQ | Consume `IngestEventEnvelope` messages |
| Realtime Hub | Push processed results to subscribers |

## Configuration

| Key | Description |
|---|---|
| `Redis:ConnectionString` | Redis host:port |
| `RabbitMQ:Uri` | RabbitMQ AMQP URI |
| `ConnectionStrings:Postgres` | PostgreSQL connection string |
| `Hub:Url` | Internal URL of Realtime Hub (e.g., `http://realtime-hub:5200`) |

## Subscription lifecycle

Event subscriptions are managed via admin endpoints on Request API (`/api/v1/admin/subscriptions`). When a dashboard definition is deleted, all of its subscriptions are automatically removed via PostgreSQL `ON DELETE CASCADE` on the `event_subscriptions` table.

## Failure handling

- Invalid payloads (schema violation) are moved to a dead-letter queue after max retries.
- Subscriptions with no active connections are silently skipped — no error.
- RabbitMQ message is ACK'd after all subscriptions have been processed (or DLQ'd on unrecoverable error).
