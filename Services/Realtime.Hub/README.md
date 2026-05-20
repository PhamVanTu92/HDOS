# Realtime Hub

ASP.NET Core SignalR hub for real-time push notifications. Delivers request progress events and completion results to connected browser clients. Horizontally scalable via Redis backplane.

**Port:** 5200 (internal — access via Gateway at port 5500, path `/hubs/main`)

---

## Responsibilities

- Host the `/hubs/main` SignalR endpoint for browser WebSocket/SSE/long-poll connections.
- Authenticate incoming connections using JWT (same OIDC authority as other services).
- Route incoming `hub.invoke("SubmitRequest", ...)` calls to Request API (alternative to HTTP POST).
- Receive push messages from Response Dispatcher and Progress Dispatcher Workers (via Redis pub/sub backplane).
- Deliver `RequestCompleted`, `RequestFailed`, and `ProgressUpdate` messages to the correct user's connections.

## Fan-out strategy

1. Push to the specific `connectionId` that originated the request.
2. If that connection is gone (tab closed), fall back to user-level group `user:{userId}` — all tabs for that user receive the push.
3. Frontend tabs ignore pushes for `requestId`s they didn't originate (see `PROTOCOL.md §3`).

## Hub methods (server → client)

| Method | Payload | Description |
|---|---|---|
| `RequestCompleted` | `{ requestId, result }` | Terminal — request succeeded |
| `RequestFailed` | `{ requestId, error, errorCode }` | Terminal — request failed |
| `ProgressUpdate` | `{ requestId, percent, message }` | Non-terminal progress event |

## Key dependencies

| Dependency | Purpose |
|---|---|
| Redis | SignalR backplane (multi-instance fan-out) + connection state |
| RabbitMQ | Consume push messages from Workers |

## Configuration

| Key | Description |
|---|---|
| `Redis:ConnectionString` | Redis host:port (used for SignalR backplane) |
| `RabbitMQ:Uri` | RabbitMQ AMQP URI |
| `Auth:Authority` | OIDC authority for JWT validation |
| `Auth:Audience` | Expected JWT audience |

## Health check

`GET /healthz/live` → 200.

## Scaling

Multiple instances can run behind the gateway. The Redis backplane ensures that a push published by any worker is delivered to the hub instance holding the target connection.
