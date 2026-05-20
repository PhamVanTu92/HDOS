# Response Dispatcher Worker

Background worker that persists completed operation results and delivers them to frontend clients via SignalR push.

---

## Responsibilities

- Consume `OperationCompleted` and `OperationFailed` messages from RabbitMQ.
- Persist the result payload to PostgreSQL (for future polling via `GET /api/v1/requests/{id}/result`).
- Update the request status in Redis cache.
- Push `RequestCompleted` or `RequestFailed` to the connected client via Realtime Hub:
  1. Try `connectionId`-targeted push first.
  2. Fall back to user-level group `user:{userId}` if the connection is gone.

## Message flow

```
RabbitMQ (OperationCompleted | OperationFailed)
  → Response Dispatcher Worker
    → PostgreSQL (persist result)
    → Redis (update status cache)
    → Realtime Hub (SignalR push → browser)
```

## Key dependencies

| Dependency | Purpose |
|---|---|
| PostgreSQL | Persist final results |
| Redis | Update cached request status |
| RabbitMQ | Consume `OperationCompleted` / `OperationFailed` |
| Realtime Hub | SignalR push (HTTP call to Hub internal API) |

## Configuration

| Key | Description |
|---|---|
| `Redis:ConnectionString` | Redis host:port |
| `RabbitMQ:Uri` | RabbitMQ AMQP URI |
| `ConnectionStrings:Postgres` | PostgreSQL connection string |

## Idempotency

Results are upserted (not inserted) to PostgreSQL. Duplicate `OperationCompleted` messages (RabbitMQ at-least-once) result in an idempotent re-push — frontend deduplicates by `requestId`.
