# PHASE_7_PLAN.md — Frontend Gateway Services
> Status: APPROVED | Author: Claude | Date: 2026-05-19

Phase 7 exposes the platform to clients. Four services are introduced:

| Service | Role |
|---|---|
| `Request.Api` | HTTP submission, SSE progress, result-fetch, cancel |
| `Realtime.Hub` | SignalR WebSocket transport (submit, cancel, subscribe, push) |
| `Response.Dispatcher.Worker` | Consumes `OperationResponseMessage`; routes terminal results to clients |
| `Progress.Dispatcher.Worker` | Relays progress events from Redis Streams to SSE connections |

**Wait for approval of this plan before writing any `.cs` files.**

---

## §1 Project Structure

```
Services/
  Request.Api/
    Request.Api.csproj
    Program.cs
    Controllers/
      RequestsController.cs          ← POST /api/v1/requests, GET /{id}/result, POST /{id}/cancel
    Sse/
      SseProgressEndpoint.cs         ← GET /sse/requests/{id}/progress
      SseConnectionRegistry.cs       ← thread-safe: requestId → List<SseChannel>
    Options/
      ApiOptions.cs                  ← RateLimitWindow, PerUserMax, PerTenantMax, ResultFetchMaxPoll
    appsettings.json
    appsettings.Development.json

  Realtime.Hub/
    Realtime.Hub.csproj
    Program.cs
    Hubs/
      MainHub.cs                     ← IConsumer methods + IHubContext push registration
    Options/
      HubOptions.cs                  ← MaxConnectionsPerUser, BackplaneOptions
    appsettings.json
    appsettings.Development.json

  Response.Dispatcher.Worker/
    Response.Dispatcher.Worker.csproj
    Program.cs
    Consumers/
      OperationResponseConsumer.cs   ← consumes OperationResponseMessage from RabbitMQ
    Services/
      ResponseRouter.cs              ← orchestrates: owner-lookup → push → result-store → cleanup
    Options/
      DispatcherOptions.cs           ← FallbackToUserGroup, ResultStoreTtlSeconds
    appsettings.json
    appsettings.Development.json

  Progress.Dispatcher.Worker/
    Progress.Dispatcher.Worker.csproj
    Program.cs
    Workers/
      ProgressRelayWorker.cs         ← BackgroundService; reads Redis Streams, publishes to pub/sub
    Options/
      ProgressOptions.cs             ← StreamPollIntervalMs, MaxEventsPerBatch

tests/
  Gateway.Tests/
    Gateway.Tests.csproj
    Helpers/
      GatewayTestHelpers.cs          ← fakes: FakeSignalRContext, RecordingHubClients, FakeOwnerStore, etc.
    Submission/
      DualTransportSubmissionTests.cs ← DT1–DT8: parity tests (HTTP path + Hub path, same assertion)
    Hub/
      MainHubTests.cs                ← HB1–HB5: Hub-specific tests
    Sse/
      SseProgressEndpointTests.cs    ← SS1–SS4: SSE tests
    Dispatcher/
      ResponseDispatcherTests.cs     ← RD1–RD6: routing, fan-out, result-store, orphan
```

**Project references** (all 4 services):
- `Shared/Operations` — `RequestSubmissionService`, `IOperationBus`, `IIdempotencyService`
- `Shared/Contracts` — message types, `SubmitAck`, `RequestEnvelope`
- `Shared/Caching` — `OwnerStore`, `ResultStore`, `RedisKeys`
- `Shared/Telemetry`, `Shared/Messaging`

**Additional packages** (new in Phase 7):
- `Microsoft.AspNetCore.SignalR.StackExchangeRedis 9.0.x` — Redis backplane (Realtime.Hub + Response.Dispatcher.Worker)
- `MessagePack 2.5.x` + `Microsoft.AspNetCore.SignalR.Protocols.MessagePack 9.0.x` — Hub binary protocol
- `Microsoft.AspNetCore.RateLimiting 9.0.x` — per-user / per-tenant rate limits
- `Microsoft.AspNetCore.Authentication.JwtBearer 9.0.x` — all 4 services
- `MassTransit.RabbitMQ 8.2.5` — Response.Dispatcher.Worker (consumer)

---

## §2 SignalR Hub Design (`Realtime.Hub`)

### 2.1 Hub endpoint and protocol

```
Endpoint : /hubs/main
Protocol : MessagePack (binary, required — JSON fallback disabled)
Backplane: StackExchangeRedis (transparent to client)
```

Hub wiring in `Program.cs`:
```csharp
builder.Services
    .AddSignalR(o => o.EnableDetailedErrors = isDev)
    .AddMessagePackProtocol()
    .AddStackExchangeRedis(redisConnStr, o =>
        o.Configuration.ChannelPrefix = RedisChannel.Literal("rp:hub"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Hub: bearer token from query-string (?access_token=) for SignalR negotiation
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

app.MapHub<MainHub>("/hubs/main").RequireAuthorization();
```

### 2.2 Client → Server methods (4 total)

All methods are `[HubMethodName]`-decorated in `MainHub : Hub`. Each validates JWT claims before executing.

#### `SubmitRequest(RequestEnvelope envelope) → SubmitAck`

```csharp
[HubMethodName("SubmitRequest")]
public async Task<SubmitAck> SubmitRequestAsync(RequestEnvelope envelope)
{
    ValidateCallerTenant(envelope.TenantId);   // throws HubException "FORBIDDEN" if mismatch
    var connectionId = Context.ConnectionId;
    return await _submissionService.SubmitAsync(envelope, connectionId, Context.ConnectionAborted);
}
```

`ValidateCallerTenant`: compares `envelope.TenantId` against the `tenant` claim in `Context.User`. Mismatch = `HubException("FORBIDDEN")`. Same check performed in `RequestsController`.

Error translation: `OperationException` codes map to `HubException(message: code, data: detail)` via a Hub filter (`HubExceptionFilter`). The filter is registered globally so HTTP exceptions are NOT swallowed here.

#### `CancelRequest(string requestId) → void`

```csharp
[HubMethodName("CancelRequest")]
public async Task CancelRequestAsync(string requestId)
{
    var userId = Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;
    await _cancelBus.PublishCancelAsync(requestId, userId, Context.ConnectionAborted);
}
```

`ICancelBus.PublishCancelAsync` publishes `CancelRequestMessage` to `reporting.cancel-requests`.
Response.Dispatcher.Worker interprets `CancelRequestMessage` and publishes `OperationResponseMessage(Status=Cancelled)` — see §6.

#### `SubscribeWidget(string channel) → void`

```csharp
[HubMethodName("SubscribeWidget")]
public async Task SubscribeWidgetAsync(string channel)
{
    ValidateWidgetChannel(channel); // format: "widget:{dashboardCode}:{widgetId}"
    await Groups.AddToGroupAsync(Context.ConnectionId, channel);
}
```

`WidgetStale` events are later pushed to this group by the ingestion pipeline (Phase 8).

#### `UnsubscribeWidget(string channel) → void`

```csharp
[HubMethodName("UnsubscribeWidget")]
public Task UnsubscribeWidgetAsync(string channel) =>
    Groups.RemoveFromGroupAsync(Context.ConnectionId, channel);
```

### 2.3 Strongly-typed hub client interface (`IMainHubClient`)

`MainHub` uses the strongly-typed generic form `Hub<IMainHubClient>`, defined in `Shared/HubContracts`. This gives compile-time safety on all push method names — no raw `"RequestCompleted"` strings anywhere in production code.

**`Shared/HubContracts/IMainHubClient.cs`**:
```csharp
public interface IMainHubClient
{
    Task RequestCompleted(ResponseDispatchPushMessage push);
    Task RequestFailed(ResponseDispatchPushMessage push);
    Task RequestCancelled(ResponseDispatchPushMessage push);
    Task WidgetStale(string channel, WidgetStaleHint hint);
}
```

**Hub declaration**:
```csharp
public sealed class MainHub : Hub<IMainHubClient>
{
    // Context.Clients is now IHubCallerClients<IMainHubClient> — fully typed
}
```

**`Response.Dispatcher.Worker`** uses `IHubContext<MainHub, IMainHubClient>`:
```csharp
// Injected as IHubContext<MainHub, IMainHubClient> — typed, no string method names
await _hubContext.Clients.Client(connId).RequestCompleted(push);
await _hubContext.Clients.Group($"user:{userId}").RequestFailed(push);
```

### 2.4 Server → Client push (4 event names)

These methods are defined on `IMainHubClient` and called by `Response.Dispatcher.Worker` via `IHubContext<MainHub, IMainHubClient>`:

| Method | Payload type | When |
|---|---|---|
| `RequestCompleted` | `ResponseDispatchPushMessage` | `Status = Done` |
| `RequestFailed` | `ResponseDispatchPushMessage` | `Status = Failed \| Timeout` |
| `RequestCancelled` | `ResponseDispatchPushMessage` | `Status = Cancelled` |
| `WidgetStale` | `(string channel, WidgetStaleHint hint)` | Data-driven invalidation (Phase 8) |

`ResponseDispatchPushMessage` (MessagePack-annotated, lives in `Shared/HubContracts`):
```csharp
[MessagePackObject]
public sealed record ResponseDispatchPushMessage
{
    [Key("requestId")]   public required string       RequestId   { get; init; }
    [Key("status")]      public required string       Status      { get; init; }
    [Key("operation")]   public required string       Operation   { get; init; }
    [Key("payload")]     public          string?      PayloadJson { get; init; } // raw JSON string
    [Key("error")]       public          ErrorDetail? Error       { get; init; }
    [Key("elapsedMs")]   public          long         ElapsedMs   { get; init; }
    [Key("tenantId")]    public required string       TenantId    { get; init; }
}
```

`ErrorDetail` (already in `Shared/Contracts/Responses/ErrorDetail.cs`) gains `[MessagePackObject]` + `[Key]` attributes in Phase 7. No new type needed.

### 2.5 Connection lifecycle hooks

```csharp
public override async Task OnConnectedAsync()
{
    // Join user-level group for fan-out fallback (DECISIONS.md — multi-tab)
    await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{UserId}");
    await base.OnConnectedAsync();
}

public override async Task OnDisconnectedAsync(Exception? exception)
{
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{UserId}");
    await base.OnDisconnectedAsync(exception);
}
```

---

## §3 HTTP API Design (`Request.Api`)

### 3.1 POST /api/v1/requests — HTTP submission

```http
POST /api/v1/requests
Authorization: Bearer <jwt>
X-Connection-Id: <signalr-connectionId>   ← optional; absent if SignalR not yet connected
Content-Type: application/json

{ RequestEnvelope }
```

**Controller action**:
```csharp
[HttpPost]
[ProducesResponseType(typeof(SubmitAck), StatusCodes.Status202Accepted)]
public async Task<IActionResult> SubmitAsync(
    [FromBody] RequestEnvelope envelope,
    [FromHeader(Name = "X-Connection-Id")] string? connectionId,
    CancellationToken ct)
{
    EnforceCallerTenant(envelope.TenantId);  // 403 if JWT tenant ≠ envelope.TenantId
    var ack = await _submissionService.SubmitAsync(envelope, connectionId, ct);
    return Accepted(ack);
}
```

**Error→HTTP mapping** (via `ProblemDetails` middleware + exception filter):

| `OperationException` code | HTTP status | Notes |
|---|---|---|
| `OPERATION_NOT_FOUND` | 400 | |
| `VALIDATION_ERROR` | 400 | include `errors` array in `ProblemDetails.Extensions` |
| `PARAMS_TOO_LARGE` | 400 | |
| `DUPLICATE_REQUEST` | 409 | |
| `RATE_LIMITED` | 429 | set `Retry-After` header |
| `BACKPRESSURE` | 503 | set `Retry-After` header |
| `FORBIDDEN` | 403 | |
| `UNAUTHORIZED` | 401 | (JWT middleware handles this before controller) |

### 3.2 GET /api/v1/requests/{id}/result — reconnection fallback

```http
GET /api/v1/requests/{id}/result
Authorization: Bearer <jwt>
```

All three response codes return a **uniform JSON envelope** with a `status` discriminator field. This lets clients branch on `status` without inspecting HTTP status codes:

```json
// 200 OK — terminal result stored
{ "status": "completed", "requestId": "01HQ7...", "result": { /* ResponseDispatchMessage */ } }

// 202 Accepted — still in flight
{ "status": "in_flight", "requestId": "01HQ7...", "submittedAt": "2026-05-19T10:00:00.000Z" }

// 404 Not Found — orphaned (submitted, result lost) or never submitted
{ "status": "orphaned", "requestId": "01HQ7..." }
{ "status": "not_found", "requestId": "01HQ7..." }
```

**Controller action**:
```csharp
[HttpGet("{requestId}/result")]
public async Task<IActionResult> GetResultAsync(string requestId, CancellationToken ct)
{
    // 1. Terminal result cached in Redis (TTL 5 min)
    var result = await _resultStore.GetAsync(requestId, ct);
    if (result is not null)
        return Ok(new { status = "completed", requestId, result = result.ResponseJson });

    // 2. Owner record present → still in flight
    var owner = await _ownerStore.GetAsync(requestId, ct);
    if (owner is not null)
        return Accepted(new { status = "in_flight", requestId, submittedAt = owner.SubmittedAt });

    // 3. Check submission log for orphan detection
    var orphanStatus = await _orphanDetector.CheckAsync(requestId, ct);
    return NotFound(new { status = orphanStatus, requestId });  // "orphaned" | "not_found"
}
```

Orphan detection algorithm: see §7.

### 3.3 POST /api/v1/requests/{id}/cancel — HTTP cancel

```http
POST /api/v1/requests/{requestId}/cancel
Authorization: Bearer <jwt>
```

```csharp
[HttpPost("{requestId}/cancel")]
public async Task<IActionResult> CancelAsync(string requestId, CancellationToken ct)
{
    var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    await _cancelBus.PublishCancelAsync(requestId, userId, ct);
    return Accepted();
}
```

`ICancelBus` is a thin interface (same pattern as `IOperationBus`) wrapping `IPublishEndpoint`:
```csharp
public interface ICancelBus
{
    Task PublishCancelAsync(string requestId, string userId, CancellationToken ct = default);
}
```

`CancelRequestMessage` is already in `Shared/Contracts/Messaging/CancelRequestMessage.cs`.

### 3.4 Rate limiting

`Microsoft.AspNetCore.RateLimiting` sliding-window policy applied to all routes in `Request.Api` and `SubmitRequest` in `Realtime.Hub`:

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddSlidingWindowLimiter("per-user", opts =>
    {
        opts.Window = TimeSpan.FromMinutes(1);
        opts.PermitLimit = apiOpts.PerUserMax;   // default 100
        opts.SegmentsPerWindow = 4;
    });
    o.AddSlidingWindowLimiter("per-tenant", opts =>
    {
        opts.Window = TimeSpan.FromMinutes(1);
        opts.PermitLimit = apiOpts.PerTenantMax; // default 500
        opts.SegmentsPerWindow = 4;
    });
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = new { code = "RATE_LIMITED", retryAfterMs = 60_000 } }, ct);
    };
});
```

The Hub maps `OnRejected` to a `HubException("RATE_LIMITED", new { retryAfterMs = 60_000 })` via a `HubFilter` that intercepts method invocation before the controller action runs.

---

## §4 Shared Submission Internals

### 4.1 Issue X — `SubmitAsync` signature (confirmed)

**Confirmed actual signature** (read from `Shared/Operations/Dispatcher/RequestSubmissionService.cs`):
```csharp
public async Task<SubmitAck> SubmitAsync(
    RequestEnvelope envelope,
    string? connectionId,
    CancellationToken ct = default)
```

`SubmissionContext` **does not exist** — it was mentioned in early planning notes but was never implemented. The plan's invocation patterns in §2.2, §3.1, and §4.2 use `(envelope, connectionId, ct)` which matches the actual signature exactly. No drift. Recorded in DECISIONS.md.

### 4.2 RequestSubmissionService — Phase 7 additions

`RequestSubmissionService` already exists in `Shared/Operations`. Phase 7 adds two responsibilities:

1. **Owner store write**: after idempotency claim succeeds (Step 6 in current code), write `OwnerStoreRecord` to Redis before publishing to the queue. This ensures Response.Dispatcher.Worker can find the connection when the response arrives.

2. **SSE URL correction**: current code emits `/api/v1/progress/{requestId}`; Phase 7 changes to `/sse/requests/{requestId}/progress` (matches PROTOCOL.md §7.1).

**Modified constructor** — add `OwnerStore` dependency + active-progress Set support:
```csharp
public RequestSubmissionService(
    IOperationRegistry operationRegistry,
    IParamsValidator paramsValidator,
    IIdempotencyService idempotency,
    IOperationBus bus,
    OwnerStore ownerStore,           // ← NEW in Phase 7
    IDatabase redis,                 // ← NEW in Phase 7 (active-progress Set + submission log)
    ILogger<RequestSubmissionService> logger)
```

**Insertion point** (after Step 6, before Step 7 in `SubmitAsync`):
```csharp
// Step 6b: Record ownership (connectionId may be null for HTTP-without-header submits)
await _ownerStore.SetAsync(new OwnerStoreRecord
{
    RequestId    = envelope.RequestId,
    ConnectionId = connectionId,
    UserId       = envelope.UserId,
    TenantId     = envelope.TenantId,
    SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
}, ct);

// Step 6c: Submission log (orphan detection — TTL = MessageTtlMs × 3 = 30 min)
await _redis.StringSetAsync(
    RedisKeys.SubmissionLog(envelope.RequestId),
    "1",
    TimeSpan.FromMilliseconds(MessageTtlMs * 3));

// Step 6d: Active-progress tracking (only when client opted in)
if (envelope.Options.Progress)
    await _redis.SetAddAsync(RedisKeys.ActiveProgress, envelope.RequestId);
```

If `connectionId` is `null` (HTTP submit without `X-Connection-Id`), the owner record still stores `UserId` so the Response.Dispatcher.Worker falls back to the user-level group.

**New `RedisKeys` entries** (add to `Shared/Caching/RedisKeys.cs`):
```csharp
// Submission log: proof of submission for orphan detection. TTL = MessageTtlMs × 3.
// Key: rp:sublog:{requestId}
public static string SubmissionLog(string requestId) => $"rp:sublog:{requestId}";

// Active-progress Set: requestIds currently expecting SSE progress.
// Key: rp:active-progress  (single Set, not per-requestId)
public const string ActiveProgress = "rp:active-progress";
```

### 4.3 Dual-transport guarantee

Both paths call the same `RequestSubmissionService.SubmitAsync(envelope, connectionId, ct)`:

| Field | HTTP path source | Hub path source |
|---|---|---|
| `envelope` | `[FromBody]` | Hub method argument |
| `connectionId` | `X-Connection-Id` header (nullable) | `Context.ConnectionId` (always present) |

The submission service cannot tell which transport was used. All validation, idempotency, owner-store write, and queue publish are identical.

**CI guarantee** (§10): every dual-transport test submits via HTTP then via Hub and asserts identical state mutations.

---

## §5 SSE Endpoint Design (`Request.Api`)

### 5.1 Endpoint

`GET /sse/requests/{requestId}/progress`

Mounted in `Program.cs` as a minimal-API route (not a controller):
```csharp
app.MapGet("/sse/requests/{requestId}/progress", SseProgressEndpoint.HandleAsync)
   .RequireAuthorization();
```

### 5.2 Authentication — query param fallback

The `EventSource` browser API cannot set custom headers. JWT is accepted via `?access_token=` query param. The `JwtBearerEvents.OnMessageReceived` hook (same pattern as Hub) reads the token from the SSE path:

```csharp
OnMessageReceived = ctx =>
{
    var path = ctx.HttpContext.Request.Path;
    if (path.StartsWithSegments("/sse") || path.StartsWithSegments("/hubs"))
    {
        var token = ctx.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(token)) ctx.Token = token;
    }
    return Task.CompletedTask;
}
```

### 5.3 SSE handler

```csharp
public static async Task HandleAsync(
    string requestId,
    HttpContext ctx,
    SseConnectionRegistry registry,
    CancellationToken ct)
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(200)
        { FullMode = BoundedChannelFullMode.DropOldest });

    registry.Register(requestId, channel.Writer);
    try
    {
        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            await ctx.Response.WriteAsync($"event: {evt.Name}\ndata: {evt.DataJson}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            if (evt.Name == "terminal") break;
        }
    }
    finally
    {
        registry.Unregister(requestId, channel.Writer);
    }
}
```

### 5.4 SseConnectionRegistry

Thread-safe mapping from `requestId` to one or more `ChannelWriter<SseEvent>` instances (one per open SSE connection for that request):

```csharp
// Internal to Request.Api — not shared across processes.
// Progress.Dispatcher.Worker publishes to Redis pub/sub;
// each API node subscribes and fans out to local channels via this registry.
internal sealed class SseConnectionRegistry
{
    private readonly ConcurrentDictionary<string, List<ChannelWriter<SseEvent>>> _connections = new();

    public void Register(string requestId, ChannelWriter<SseEvent> writer) { ... }
    public void Unregister(string requestId, ChannelWriter<SseEvent> writer) { ... }

    // Called by the local Redis pub/sub subscriber when a progress event arrives
    public async Task FanOutAsync(string requestId, SseEvent evt)
    {
        if (!_connections.TryGetValue(requestId, out var writers)) return;
        foreach (var w in writers)
            w.TryWrite(evt); // non-blocking; BoundedChannel drops oldest if full
    }
}
```

### 5.5 Progress event format

```
event: progress
data: {"requestId":"...","percent":42,"message":"step 2/5","tsUnixMs":1716030042000}

event: terminal
data: {"requestId":"...","resultUrl":"/api/v1/requests/{requestId}/result"}

```

The `terminal` event signals the client to close the SSE stream. It does NOT carry the final result — the actual terminal payload arrives on SignalR (`RequestCompleted` / `RequestFailed` / `RequestCancelled`). The `resultUrl` is a fallback: if no SignalR push arrives within 5 seconds of the `terminal` event, the client may fetch the result directly via `GET {resultUrl}` — this covers edge cases where the Hub connection dropped between the terminal push and the client receiving it.

### 5.6 Server-side ring buffer guarantee

The backend maintains progress events for up to 30 seconds for late-joining SSE clients. This is implemented via the Redis Stream at `rp:progress:{requestId}` (already established in Phase 5 via `ProgressRingBufferAdapter`). When a new SSE client connects:

1. `SseProgressEndpoint` registers its channel with the registry.
2. `Progress.Dispatcher.Worker` replays buffered events from the Redis Stream for this `requestId` before resuming live-tailing — eliminating the race between SSE-open and first progress event.

---

## §6 Response.Dispatcher.Worker Flow

### 6.1 Queue consumer

Consumes `OperationResponseMessage` from `reporting.operation-responses` (existing queue name from `QueueNames.OperationResponses`). Single consumer type, single queue — no priority needed for responses.

```csharp
public sealed class OperationResponseConsumer : IConsumer<OperationResponseMessage>
{
    public async Task Consume(ConsumeContext<OperationResponseMessage> ctx)
    {
        var msg = ctx.Message;
        _logger.LogInformation(
            "Routing response requestId={RequestId} status={Status}",
            msg.RequestId, msg.Status);
        await _router.RouteAsync(msg, ctx.CancellationToken);
    }
}
```

### 6.2 ResponseRouter — step-by-step flow

```csharp
public sealed class ResponseRouter
{
    public async Task RouteAsync(OperationResponseMessage msg, CancellationToken ct)
    {
        // Step 1: Read owner record
        var owner = await _ownerStore.GetAsync(msg.RequestId, ct);

        // Step 2: Build push message
        var push = MapToPushMessage(msg);   // OperationResponseMessage → ResponseDispatchPushMessage

        // Step 3: Push via SignalR (IHubContext<MainHub, IMainHubClient> — type-safe)
        Func<Task> pushFn = msg.Status switch
        {
            ResponseStatus.Done      => () => PushToTarget(owner, c => c.RequestCompleted(push), ct),
            ResponseStatus.Cancelled => () => PushToTarget(owner, c => c.RequestCancelled(push), ct),
            _                        => () => PushToTarget(owner, c => c.RequestFailed(push),    ct),
        };
        await pushFn();

        // Step 4: Write terminal result to ResultStore (5-min TTL for GET /result fallback)
        await _resultStore.SetAsync(new ResultStoreRecord
        {
            RequestId    = msg.RequestId,
            ResponseJson = SerializeResponse(msg),
        }, ct);

        // Step 5: Publish terminal SSE signal via Redis pub/sub
        // Each Request.Api node receives this and emits "terminal" SSE event locally.
        await _pubSub.PublishAsync(
            RedisChannel.Literal($"rp:sse-terminal:{msg.RequestId}"),
            msg.RequestId);

        // Step 6: Clean up — remove from active-progress Set and delete owner record
        await _redis.SetRemoveAsync(RedisKeys.ActiveProgress, msg.RequestId);
        await _ownerStore.DeleteAsync(msg.RequestId);
    }

    // Pushes to connectionId first; falls back to user-level group if connection is gone.
    // If no owner record, logs a warning but does NOT throw — result is stored in ResultStore.
    private async Task PushToTarget(
        OwnerStoreRecord? owner,
        Func<IMainHubClient, Task> invoke,
        CancellationToken ct)
    {
        if (owner?.ConnectionId is not null)
        {
            await invoke(_hubContext.Clients.Client(owner.ConnectionId));
        }
        else if (owner?.UserId is not null)
        {
            // Fan-out to all connections for this userId (multi-tab fallback, DECISIONS.md)
            await invoke(_hubContext.Clients.Group($"user:{owner.UserId}"));
        }
        else
        {
            _logger.LogWarning("No owner record for requestId={RequestId} — result stored only",
                owner?.RequestId ?? "unknown");
        }
    }
}
```

`PushMethodName(ResponseStatus)`:
- `Done` → `"RequestCompleted"`
- `Failed` | `Timeout` → `"RequestFailed"`
- `Cancelled` → `"RequestCancelled"`

### 6.3 IHubContext access from Worker

`Response.Dispatcher.Worker` uses `IHubContext<MainHub, IMainHubClient>` (strongly-typed per Patch 1). It references `Shared/HubContracts` (which defines `MainHub`, `IMainHubClient`, and `ResponseDispatchPushMessage`) but does NOT reference the `Realtime.Hub` assembly directly. The worker registers against the same Redis backplane channel prefix to participate in backplane routing:

```csharp
builder.Services
    .AddSignalR()
    .AddStackExchangeRedis(redisConnStr, o =>
        o.Configuration.ChannelPrefix = RedisChannel.Literal("rp:hub"));

// IHubContext<MainHub, IMainHubClient> is automatically available after AddSignalR()
// — no need to call app.MapHub<MainHub>() in the Worker (Worker doesn't host the Hub endpoint).
// The backplane routes pushes to the Realtime.Hub node that owns the connection.
```

**`Shared/HubContracts.csproj`** contents summary:
- `IMainHubClient` interface
- `ResponseDispatchPushMessage` record (MessagePack-annotated)
- `MainHub` **class declaration only** (no method implementations — those are in `Realtime.Hub`)

**Note**: `MainHub` in `Shared/HubContracts` is declared as `public sealed class MainHub : Hub<IMainHubClient> { }` with no methods. `Realtime.Hub` extends this with partial class or method injection pattern. Alternatively, `MainHub` is fully in `Realtime.Hub` and `Response.Dispatcher.Worker` uses `IHubContext<object>` with a type marker interface. OQ2 **resolved**: use `Shared/HubContracts` with the forward-declaration approach — Worker references `HubContracts`, not `Realtime.Hub`.

### 6.4 Cancel handling

`CancelRequestMessage` arrives on `reporting.cancel-requests`. Two options:
1. Response.Dispatcher.Worker also consumes `CancelRequestMessage` and publishes `OperationResponseMessage(Status=Cancelled)` immediately (best-effort, before the Router sees it).
2. Operation.Router.Worker detects cancellation via `CancellationToken` and publishes `Cancelled` naturally.

**Decision**: both happen (race). Whichever publishes first is processed; the ResultStore write is idempotent (`SET NX` or overwrite). The second write is a no-op from the client's perspective because only one `RequestCompleted/Cancelled/Failed` push is delivered per requestId via the owner-lookup flow (owner record deleted after first push — step 6 above).

Phase 7 implements: `CancelRequestConsumer` in `Response.Dispatcher.Worker` publishes a synthetic `OperationResponseMessage(Status=Cancelled)` to `reporting.operation-responses`. The response consumer then routes it normally. The Router's existing cancellation detection is a secondary path.

---

## §7 Orphan Detection Algorithm

Referenced in PROTOCOL.md §3.4 and PHASE_6_PLAN.md §2.1. Implemented in `Request.Api` as `OrphanDetector`.

### 7.1 Detection logic

```
GET /api/v1/requests/{id}/result:

1. result = ResultStore.GetAsync(requestId)          → exists: 200 OK (done)
2. owner  = OwnerStore.GetAsync(requestId)           → exists: 202 Accepted (in flight)
3. idem   = IdempotencyService.ExistsAsync(requestId) → exists: 202 Accepted (queued, owner record expired early)
4. else:
   → 404 Not Found, body: { "status": "not_found" }
   IF (client-supplied queuedAt header, or server checks submission time from archived log)
   → 404 Not Found, body: { "status": "orphaned" }
```

**Practical implementation**: the idempotency key TTL is `effectiveMs * 2` (set in `RequestSubmissionService`). After that expires, no server-side state remains for the request. The server cannot independently determine orphan vs. never-submitted without storing a third artifact.

**Phase 7 decision**: add a lightweight **submission log** key: `rp:sublog:{requestId}` with TTL = `MessageTtlMs * 3` (30 minutes). Written by `RequestSubmissionService` alongside the idempotency key. Contains only `{ submittedAt, tenantId }`.

```csharp
// In RequestSubmissionService.SubmitAsync, after idempotency claim (Step 6):
await _redis.StringSetAsync(
    RedisKeys.SubmissionLog(envelope.RequestId),
    envelope.RequestId,     // value is just a marker (the key itself carries the data via TTL)
    TimeSpan.FromMilliseconds(opts.MessageTtlMs * 3));
```

`OrphanDetector.CheckAsync`:
```csharp
public async Task<string> CheckAsync(string requestId, string tenantId, CancellationToken ct)
{
    // Is there a submission log entry? If yes, the request was submitted but has no result.
    // If the log entry is still present (within 30-min window), it was submitted but
    // we have no result — treat as orphaned (message TTL likely fired).
    var exists = await _redis.KeyExistsAsync(RedisKeys.SubmissionLog(requestId));
    return exists ? "orphaned" : "not_found";
}
```

This returns `{ "status": "orphaned" }` HTTP 404 for requests that were genuinely submitted but whose result has been lost (broker TTL fired, idempotency expired, no SignalR push delivered). Clients that see `"orphaned"` must generate a new `requestId`.

---

## §8 Progress.Dispatcher.Worker

### 8.1 Architecture — why not a separate process

SSE connections are local to the `Request.Api` process. If `Progress.Dispatcher.Worker` were a separate process, it could not directly write to SSE clients on a different API node.

**Design**: `Progress.Dispatcher.Worker` is a **separate process** that reads from Redis Streams and **publishes to Redis pub/sub**. Each `Request.Api` node subscribes to the pub/sub channel for each active `requestId` and fans out to local SSE connections via `SseConnectionRegistry`. This avoids IPC between processes and scales horizontally.

```
Redis Stream (rp:progress:{requestId})
    ↓ read by Progress.Dispatcher.Worker (one instance, or competing consumers via consumer groups)
    ↓ publish to Redis pub/sub channel: rp:sse-notify:{requestId}
    ↓ each Request.Api node receives pub/sub message
    ↓ SseConnectionRegistry.FanOutAsync(requestId, evt) → local ChannelWriter<SseEvent>
```

### 8.2 Active-progress tracking (Redis Set `rp:active-progress`)

**Decision (Patch 2)**: `ProgressRelayWorker` discovers which requests have active progress streams via a Redis Set, not via SCAN. SCAN is O(N) on keyspace and non-deterministic; a dedicated Set is O(1) add/remove and O(M) membership (where M = number of active progress requests, typically < 100).

**Lifecycle**:

| Event | Operation |
|---|---|
| `RequestSubmissionService.SubmitAsync` (when `options.progress: true`) | `SADD rp:active-progress {requestId}` |
| `ResponseRouter` Step 6 (terminal response dispatched) | `SREM rp:active-progress {requestId}` |
| Background reaper (every 10 min) | Cross-check Set members against `rp:sublog:{requestId}` existence; remove members whose submission log has expired (stale entries from abnormal worker shutdown) |

**`ProgressRelayWorker`**:
```csharp
public sealed class ProgressRelayWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Poll active-progress Set every 5s (short enough to pick up new requests quickly)
            var activeIds = await _redis.SetMembersAsync(RedisKeys.ActiveProgress);

            foreach (var requestId in activeIds.Select(v => (string)v!))
            {
                // Read new entries from this request's Redis Stream (XREAD from last-seen ID)
                var entries = await ReadNewEntriesAsync(requestId, stoppingToken);
                foreach (var entry in entries)
                    await _pubSub.PublishAsync(
                        RedisChannel.Literal($"rp:sse-notify:{requestId}"),
                        entry.PayloadJson);
            }

            await Task.Delay(_opts.StreamPollIntervalMs, stoppingToken); // default 5000ms
        }
    }
}
```

**Last-seen ID tracking**: `ProgressRelayWorker` maintains an in-memory `ConcurrentDictionary<string, string>` mapping `requestId → lastStreamId`. On startup, it resets to `"0-0"` (replay all). This means a worker restart replays all buffered progress events — acceptable because SSE clients de-duplicate by percent (monotonically increasing).

**Terminal propagation**: When `Response.Dispatcher.Worker` writes the terminal result, it publishes to `rp:sse-terminal:{requestId}` (§6.2 Step 5). `ProgressRelayWorker` also subscribes to this channel and publishes a `terminal` SSE event — OR the Request.Api node subscribes directly to `rp:sse-terminal:{requestId}` and calls `SseConnectionRegistry.FanOutAsync` with the terminal event. The latter is simpler and avoids double-subscription.

### 8.3 Request.Api — Redis pub/sub subscriber

Each `Request.Api` node runs a `BackgroundService` (`ProgressPubSubSubscriber`) that subscribes to `rp:sse-notify:*` and `rp:sse-terminal:*` patterns via StackExchange.Redis pattern subscriptions:

```csharp
await _subscriber.SubscribeAsync(
    RedisChannel.Pattern("rp:sse-notify:*"),
    (channel, value) =>
    {
        var requestId = ExtractRequestId(channel); // strip "rp:sse-notify:" prefix
        var evt = JsonSerializer.Deserialize<SseEvent>(value!);
        _ = _registry.FanOutAsync(requestId, evt!);
    });

await _subscriber.SubscribeAsync(
    RedisChannel.Pattern("rp:sse-terminal:*"),
    (channel, value) =>
    {
        var requestId = ExtractRequestId(channel);
        _ = _registry.FanOutAsync(requestId, new SseEvent("terminal",
            $"{{\"requestId\":\"{requestId}\",\"status\":\"done\"}}"));
    });
```

---

## §9 Cancellation Flow

Full lifecycle — two parallel paths:

```
Client (HTTP or Hub)
    → POST /cancel or hub.invoke("CancelRequest", id)
    → ICancelBus.PublishCancelAsync(requestId, userId)
    → CancelRequestMessage published to reporting.cancel-requests

Path A — Response.Dispatcher.Worker (CancelRequestConsumer):
    → synthesizes OperationResponseMessage(Status=Cancelled, RequestId=...)
    → publishes to reporting.operation-responses
    → ResponseRouter.RouteAsync → push RequestCancelled to client
    → ResultStore.SetAsync (cancel result)

Path B — Operation.Router.Worker:
    → OperationRequestConsumer.Consume CancellationToken fires (if not yet dequeued)
    → OR handler's CancellationToken fires (if mid-execution)
    → OperationDispatcher returns ResponseStatus.Cancelled (or Timeout if handler doesn't check)
    → publishes OperationResponseMessage(Status=Cancelled)
    → ResponseRouter.RouteAsync → push RequestCancelled OR RequestFailed to client

Winner: whichever publishes the OperationResponseMessage first.
Loser: second OperationResponseMessage is routed, but owner record already deleted (Step 6)
       → no duplicate SignalR push. ResultStore.SetAsync is idempotent (overwrites same key).
```

The client NEVER receives two terminal pushes for the same `requestId` because:
1. `ResponseRouter` deletes the owner record after the first successful push (Step 6).
2. If owner record is absent, the router still writes to ResultStore but skips the SignalR push.

---

## §10 Test Plan (tests/Gateway.Tests/)

### Dual-transport parity tests (DT1–DT8)

Each DT test runs the same scenario via **both** HTTP and Hub paths and asserts identical observable outcomes.

| # | Scenario | Assert |
|---|---|---|
| DT1 | Happy path submit — valid envelope | Both paths return `SubmitAck` with same `requestId`; owner store has entry |
| DT2 | Duplicate `requestId` | Both paths return `SubmitAck` (idempotency); NO second queue publish |
| DT3 | Unknown `operation` | HTTP: 400 `OPERATION_NOT_FOUND`; Hub: `HubException("OPERATION_NOT_FOUND")` |
| DT4 | `params` validation failure | HTTP: 400 `VALIDATION_ERROR` with field errors; Hub: `HubException("VALIDATION_FAILED")` with `data.errors` |
| DT5 | `params` > 64 KB | HTTP: 400 `PARAMS_TOO_LARGE`; Hub: `HubException("PARAMS_TOO_LARGE")` |
| DT6 | Tenant claim mismatch (envelope.tenantId ≠ JWT.tenant) | HTTP: 403; Hub: `HubException("FORBIDDEN")` |
| DT7 | Rate limit exceeded | HTTP: 429 + `Retry-After: 60`; Hub: `HubException("RATE_LIMITED")` with `retryAfterMs` |
| DT8 | `options.progress: true` → `progressStreamUrl` set | Both paths return non-null `SubmitAck.ProgressStreamUrl` |

**Test strategy**: use `RequestSubmissionService` directly with fakes — no real HTTP or WebSocket needed for DT1–DT8. Each test instantiates the service twice (with different caller contexts) and compares outputs.

### Hub-specific tests (HB1–HB5)

| # | Test | Assert |
|---|---|---|
| HB1 | `SubscribeWidget` with valid channel | `Groups.AddToGroupAsync` called with channel name |
| HB2 | `SubscribeWidget` with malformed channel (no `widget:` prefix) | `HubException("VALIDATION_ERROR")` |
| HB3 | `UnsubscribeWidget` | `Groups.RemoveFromGroupAsync` called |
| HB4 | `OnConnectedAsync` | User-level group join (`user:{userId}`) |
| HB5 | `CancelRequest` | `ICancelBus.PublishCancelAsync` called once with correct `requestId` |

### SSE tests (SS1–SS5)

| # | Test | Assert |
|---|---|---|
| SS1 | SSE open → receive 3 progress events → terminal event → stream closes | All events delivered in order; stream closed after terminal |
| SS2 | SSE open before submit — replay of buffered events | Late-join SSE receives buffered events from Redis Stream before live events |
| SS3 | SSE open with no `options.progress` — receives terminal only | No progress events; only terminal event; stream closes |
| SS4 | SSE with expired token → 401 | Response is 401 before stream opens |
| SS5 | Mixed progress + terminal ordering preserved end-to-end | 5 progress events (percents 20, 40, 60, 80, 99) arrive in order; terminal arrives last; no events arrive after terminal |

### Response Dispatcher tests (RD1–RD7)

| # | Test | Assert |
|---|---|---|
| RD1 | Response with known connectionId → push to that connection | Typed `RequestCompleted(push)` called on `Clients.Client(id)` |
| RD2 | Response with unknown connectionId (gone) → user-level group fallback | Typed `RequestCompleted(push)` called on `Clients.Group("user:{userId}")` |
| RD3 | Response with no owner record at all → result stored, no push attempted | `ResultStore.SetAsync` called; no `IMainHubClient` method invoked |
| RD4 | Response status=Done → `RequestCompleted`; Failed → `RequestFailed`; Cancelled → `RequestCancelled` | Typed method on `IMainHubClient` matches status in all 3 cases |
| RD5 | Result written to ResultStore with correct payload | `GetAsync(requestId)` returns stored terminal |
| RD6 | Owner record deleted after routing | `OwnerStore.GetAsync(requestId)` returns null after dispatch |
| RD7 | HTTP submit with `X-Connection-Id` → `OwnerStoreRecord.ConnectionId` set → response targets that specific connectionId, NOT user group | `Clients.Client(id)` called; `Clients.Group(...)` NOT called |

### Orphan detection test (OR1–OR2)

| # | Test | Assert |
|---|---|---|
| OR1 | GET /result with submission log present, no result → 404 `{ status: "orphaned" }` | Response has `status = "orphaned"` |
| OR2 | GET /result with no submission log, no result → 404 `{ status: "not_found" }` | Response has `status = "not_found"` |

### Multi-tab / fan-out tests (MT1–MT2)

| # | Test | Assert |
|---|---|---|
| MT1 | Three connections for same `userId`; connectionId-targeted push fails (connection gone); all 3 receive push via user group | All 3 `RecordingHubClients` entries receive the message |
| MT2 | Hub disconnect mid-flight: connection B disconnects; connections A and C remain for same `userId`; response dispatched → `user:{userId}` group push | Only `RequestCompleted` pushed once to group; no error; A and C both receive it |

### Cancel-race test (CR1–CR2)

| # | Test | Assert |
|---|---|---|
| CR1 | Cancel arrives BEFORE operation completes → only `RequestCancelled` push | `RequestCompleted` NOT pushed; `RequestCancelled` pushed once |
| CR2 | Cancel arrives AFTER operation completes → only `RequestCompleted` push | Cancel is a no-op; second publish has no owner record → no push |

---

## §11 Authentication

### 11.1 JWT validation (all 4 services)

Shared configuration block added to `Shared/Telemetry` (or a new `Shared/Auth` project — see §12.OQ3):

```csharp
public static IServiceCollection AddPlatformAuth(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.Authority = configuration["Auth:Authority"];  // e.g., https://login.microsoftonline.com/...
            o.Audience  = configuration["Auth:Audience"];
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                NameClaimType            = ClaimTypes.NameIdentifier,
                RoleClaimType            = ClaimTypes.Role,
            };
            // Hub + SSE: accept token from query string
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var path = ctx.HttpContext.Request.Path;
                    if (path.StartsWithSegments("/hubs") ||
                        path.StartsWithSegments("/sse"))
                    {
                        var token = ctx.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(token)) ctx.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
        });

    services.AddAuthorization();
    return services;
}
```

### 11.2 Tenant enforcement

`TenantEnforcementFilter` (action filter for controllers, Hub filter for Hub methods):
- Extracts `tenant` claim from `HttpContext.User` / `Context.User`
- Compares against `envelope.TenantId` (or URL-based tenant context)
- Throws `OperationException("FORBIDDEN")` / `HubException("FORBIDDEN")` on mismatch

No claim extraction is done inside `RequestSubmissionService` — the service trusts the caller has already validated. This keeps the service testable without JWT infrastructure.

### 11.3 Claims extracted at gateway layer

| Claim | JWT field | Extracted by |
|---|---|---|
| `userId` | `sub` | Controller/Hub (passed to submission service via `envelope.UserId`) |
| `tenantId` | `tenant` | Controller/Hub (enforced before call, not inside service) |
| `roles` | `roles` | RBAC check (Phase 7 wires `IUserRoleChecker`, replacing Phase 5 stub) |

**RBAC wiring (Phase 7)**: `RequestSubmissionService.SubmitAsync` currently has a stub comment (Step 2). Phase 7 injects `IUserRoleChecker` and the gateway extracts the user's roles from the JWT, passes them to the submission service for enforcement against `registration.RequiredRole`.

---

## §12 Open Questions — All Resolved

| # | Resolution |
|---|---|
| OQ1 | **Standalone process** ✓ — `Progress.Dispatcher.Worker` is a separate service. Redis pub/sub hop is negligible; service boundary enables independent scaling. |
| OQ2 | **`Shared/HubContracts`** ✓ — `IMainHubClient` strongly-typed interface per Patch 1. `Response.Dispatcher.Worker` references `Shared/HubContracts`, not `Realtime.Hub`. `IHubContext<MainHub, IMainHubClient>` eliminates all string method names. |
| OQ3 | **`Shared/Telemetry`** ✓ — `AddPlatformAuth` lives in `Shared/Telemetry` as `AuthExtensions.cs`. Extracted to `Shared/Auth` in Phase 11 when API key auth is added. |
| OQ4 | **Independent rate limits per service** ✓ — HTTP and Hub enforce separately. Cross-transport unified counter deferred to Phase 11. |
| OQ5 | **SSE heartbeat every 30s** ✓ — `ping` event (empty `data:` line) prevents proxy/load-balancer idle disconnection. Implemented in SSE handler via a background `Task.Delay(30s)` loop alongside the main channel reader. |

---

## §13 Phase 7 Ships When

1. All 4 services build clean (`TreatWarningsAsErrors=true`, `0 warnings, 0 errors`)
2. **All 31 Gateway.Tests pass**: DT 8 + HB 5 + SS 5 + RD 7 + OR 2 + MT 2 + CR 2 = 31 minimum
3. End-to-end smoke test: HTTP submit → SignalR push received → `GET /result` returns `{ "status": "completed", ... }`
4. End-to-end smoke test: Hub Invoke submit → progress SSE stream (5 events) → terminal SSE event → SignalR push
5. `GET /result` returns `{ "status": "orphaned" }` for a request whose submission log exists but result store is absent; returns `{ "status": "not_found" }` for unknown requestId
6. `ProgressStreamUrl` in `RequestSubmissionService` corrected to `/sse/requests/{requestId}/progress` (was `/api/v1/progress/{requestId}`)
7. PROTOCOL.md updated: §7.4 terminal event format (includes `resultUrl`), §3.4 `GET /result` uniform envelope
8. DECISIONS.md updated: OQ1–OQ5 resolutions + Issue X (SubmitAsync signature confirmation)
9. Commit: `feat(gateway): Phase 7 — Frontend Gateway Services`

---

## §14 Build Order

1. `Shared/HubContracts` — `IMainHubClient`, `ResponseDispatchPushMessage` (MessagePack), `MainHub` forward declaration
2. `Shared/Caching` (diff) — add `RedisKeys.SubmissionLog`, `RedisKeys.ActiveProgress`
3. `Shared/Operations` (diff) — add `OwnerStore` + `IDatabase` dependencies to `RequestSubmissionService`; add owner-store write, submission log write, active-progress SADD; fix `ProgressStreamUrl` to `/sse/requests/{requestId}/progress`
4. `Services/Realtime.Hub` — `MainHub : Hub<IMainHubClient>`, Redis backplane, JWT auth, rate limit Hub filter
5. `Services/Request.Api` — `RequestsController`, `SseProgressEndpoint`, `SseConnectionRegistry`, `ProgressPubSubSubscriber` BackgroundService, `OrphanDetector`, rate limiting
6. `Services/Response.Dispatcher.Worker` — `OperationResponseConsumer`, `CancelRequestConsumer`, `ResponseRouter` (typed `IHubContext<MainHub, IMainHubClient>`), active-progress SREM
7. `Services/Progress.Dispatcher.Worker` — `ProgressRelayWorker` (Redis Stream → pub/sub), background reaper
8. `tests/Gateway.Tests` — all 31 tests (DT1–DT8, HB1–HB5, SS1–SS5, RD1–RD7, OR1–OR2, MT1–MT2, CR1–CR2)
