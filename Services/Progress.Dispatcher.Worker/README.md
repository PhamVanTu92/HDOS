# Progress Dispatcher Worker

Background worker that fans provider progress events out to connected frontend clients in real time via SignalR.

---

## Responsibilities

- Consume `ProgressPublished` messages from RabbitMQ.
- Push `ProgressUpdate` to the target client via Realtime Hub (SignalR):
  1. Try `connectionId`-targeted push.
  2. Fall back to user-level group `user:{userId}`.
- Does NOT persist progress events — they are ephemeral. Clients that reconnect will not receive missed progress events (they should call the polling endpoint for the final result instead).

## Message flow

```
RabbitMQ (ProgressPublished)
  → Progress Dispatcher Worker
    → Realtime Hub (SignalR push → browser: ProgressUpdate)
```

## Key dependencies

| Dependency | Purpose |
|---|---|
| Redis | SignalR backplane state (to know which hub instance holds the connection) |
| RabbitMQ | Consume `ProgressPublished` messages |

## Configuration

| Key | Description |
|---|---|
| `Redis:ConnectionString` | Redis host:port |
| `RabbitMQ:Uri` | RabbitMQ AMQP URI |

## Late progress events

Progress events may arrive after the terminal `RequestCompleted` / `RequestFailed` event due to RabbitMQ ordering across queues. Frontend clients MUST ignore `ProgressUpdate` messages received after the terminal event for a given `requestId`. See `DECISIONS.md` for rationale.
