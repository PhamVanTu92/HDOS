# Operation Router Worker

Background worker that routes incoming operation requests to the appropriate provider via Provider Bridge. The routing decision is based on the dashboard's configured provider registration.

---

## Responsibilities

- Consume `OperationRequested` messages from RabbitMQ.
- Look up the dashboard definition and its assigned provider in PostgreSQL.
- Forward the request to Provider Bridge (gRPC `ExecuteRequest`).
- Handle provider timeout and error responses: update request status in PostgreSQL, publish `OperationFailed`.
- Apply deadline propagation — the original request deadline is forwarded to Provider Bridge and the provider.

## Message flow

```
RabbitMQ (OperationRequested)
  → Operation Router Worker
    → PostgreSQL (lookup dashboard + provider)
    → Provider Bridge gRPC (ExecuteRequest)
      → External Provider (streaming)
        → ProgressPublished messages (via Provider Bridge → RabbitMQ)
        → OperationCompleted (via Provider Bridge → RabbitMQ)
```

## Key dependencies

| Dependency | Purpose |
|---|---|
| PostgreSQL | Dashboard and provider metadata lookup |
| Redis | Distributed lock (prevent duplicate routing) |
| RabbitMQ | Consume `OperationRequested`; publish `OperationFailed` |
| Provider Bridge | gRPC call to execute the request |

## Configuration

| Key | Description |
|---|---|
| `ConnectionStrings:RabbitMQ` | RabbitMQ AMQP URI |
| `ConnectionStrings:Postgres` | PostgreSQL connection string |
| `ConnectionStrings:Redis` | Redis host:port |

## Failure handling

- If Provider Bridge is unreachable: request is marked `Failed` with error code `BRIDGE_UNAVAILABLE`.
- If provider returns an error: request is marked `Failed` with the provider's error code.
- If deadline expires before a result: request is marked `Failed` with error code `DEADLINE_EXCEEDED`.
- Message is not acknowledged (NACK) on transient errors, allowing RabbitMQ to retry with backoff.
