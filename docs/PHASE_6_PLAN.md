# PHASE_6_PLAN.md — Operation Router Worker
> Status: APPROVED | Author: Claude | Date: 2026-05-19

Phase 6 wires the dispatcher built in Phase 5 to RabbitMQ. Incoming `OperationRequestMessage`s
arrive on three priority queues; the consumer calls `OperationDispatcher.DispatchAsync()` and
publishes the resulting `OperationResponseMessage` downstream. Nothing new is invented — this
phase is pure plumbing.

---

## §1 Project structure

```
Services/
  Operation.Router.Worker/
    Operation.Router.Worker.csproj
    Program.cs
    Consumers/
      OperationRequestConsumer.cs     ← single consumer type, registered 3×
    Health/
      RabbitMqHealthCheck.cs          ← custom check (MassTransit bus probe)
    Options/
      RouterOptions.cs                ← typed config (prefetch, concurrency limits)
    appsettings.json
    appsettings.Development.json

tests/
  Router.Tests/
    Router.Tests.csproj
    Consumers/
      OperationRequestConsumerTests.cs
```

**Project references**: `Shared/Operations`, `Shared/Contracts`, `Shared/Telemetry`,
`Shared/Caching` (for `IProgressBuffer` resolution at startup)

**Packages** (no new versions beyond what Operations already pins):
- `MassTransit.RabbitMQ 8.2.5`
- `Microsoft.Extensions.Hosting 9.0.4`
- `Microsoft.Extensions.Diagnostics.HealthChecks 9.0.4`
- `AspNetCore.HealthChecks.Rabbitmq` or custom probe (see §3)

---

## §2 MassTransit consumer

### Queue topology

Phase 5 `RequestSubmissionService` publishes with routing keys:
- `operation.request.high`
- `operation.request.normal`
- `operation.request.low`

The Router declares one durable exchange `operation.request` (direct) and three durable queues
bound to it:

| Routing key | Queue name | Notes |
|---|---|---|
| `operation.request.high` | `op-request-high` | Processed first by RabbitMQ priority — NOT by per-queue consumer precedence |
| `operation.request.normal` | `op-request-normal` | |
| `operation.request.low` | `op-request-low` | |

All three queues bind the same consumer type `OperationRequestConsumer`. Separate queue
registrations mean RabbitMQ's prefetch applies per-queue and high-priority work is not
head-of-line blocked behind slow low-priority work.

Dead-letter exchange: `operation.request.dlq` (direct, durable). Queues configure
`x-dead-letter-exchange = operation.request.dlq` + `x-message-ttl` from `RouterOptions.MessageTtlMs`
(default 600 000 ms — 10 minutes; matches MaxTimeoutMs hard cap from Phase 5).

### Consumer class

```csharp
// Shared/Operations/Dispatcher/OperationDispatcher already does all the real work.
// The consumer is a thin shell: receive → dispatch → publish response.

public sealed class OperationRequestConsumer : IConsumer<OperationRequestMessage>
{
    private readonly OperationDispatcher _dispatcher;
    private readonly IPublishEndpoint    _publish;
    private readonly ILogger<OperationRequestConsumer> _logger;

    public async Task Consume(ConsumeContext<OperationRequestMessage> ctx)
    {
        var msg = ctx.Message;
        _logger.LogInformation(
            "Routing {Operation} requestId={RequestId} tenantId={TenantId}",
            msg.Operation, msg.RequestId, msg.TenantId);

        var response = await _dispatcher.DispatchAsync(msg, ctx.CancellationToken);

        _logger.LogInformation(
            "Completed {Operation} requestId={RequestId} status={Status} elapsedMs={ElapsedMs}",
            msg.Operation, msg.RequestId, response.Status, response.ElapsedMs);

        await _publish.Publish(response, ctx.CancellationToken);
    }
}
```

No explicit progress publishing. Progress events flow through `IProgressBuffer` →
`ProgressRingBufferAdapter` → Redis (as established in Phase 5). Phase 7 reads from Redis
and fans out to SignalR. The consumer does not touch progress directly.

### §2.1 Message TTL vs Idempotency TTL coordination

**Scenario**: a message sits in the queue long enough for the idempotency TTL to expire before
the Router consumes it. The sequence:

1. Client submits `requestId=X` at T=0. `RequestSubmissionService` claims idempotency key
   with TTL = `effectiveMs * 2` (e.g., 60 s).
2. Broker is backlogged. Message sits in `op-request-normal` for 65 s.
3. Router dequeues at T=65 s. Idempotency claim for `X` has expired.
4. Router dispatches successfully → `OperationResponseMessage(Status=Done)` published.
5. Phase 7 Response Dispatcher stores result in Redis (ResultStore TTL = 5 min default).
6. Client re-submits `requestId=X` at T=70 s (client timeout fired). `RequestSubmissionService`
   re-claims the idempotency key (expired → TryClaimAsync returns `true`) → publishes again.
7. Router dispatches again → second result published (duplicate execution).

**Phase 6 mitigation**: `RouterOptions.MessageTtlMs` (default 600 000 ms / 10 min) matches
`MaxTimeoutMs` (Phase 5 hard cap). A message older than its own deadline will be rejected by
`OperationDispatcher.DispatchAsync()` as `DEADLINE_EXCEEDED` before any handler runs — the
response is published immediately as a timeout, not a duplicate execution.

**Residual orphan scenario**: message TTL fires (broker drops the message) AND idempotency TTL
has expired AND SignalR connection is gone. Client is left with no result and no error.

**Recovery rule** (Phase 7 responsibility to implement in Response Dispatcher):

> `GET /api/v1/requests/{id}/result` returns `404`. If the client has a local `queuedAt`
> timestamp and `now - queuedAt > MessageTtlMs * 2` (default > 20 minutes), treat the request
> as **orphaned**. The client should surface a "Request lost — please retry" error and submit
> a **new `requestId`**. Never reuse the original `requestId` after an orphan.

This rule is documented in `PROTOCOL.md §3.4` (see below). Phase 7 plan MUST implement the
404 + age check in the result endpoint and return a `{ status: "orphaned" }` discriminator
so clients can distinguish TTL-expired-but-complete from genuinely lost.

### MassTransit configuration (in `Program.cs`)

```csharp
services.AddMassTransit(x =>
{
    x.AddConsumer<OperationRequestConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbitUri);

        // One ReceiveEndpoint per priority queue
        foreach (var queue in new[] { "op-request-high", "op-request-normal", "op-request-low" })
        {
            cfg.ReceiveEndpoint(queue, ep =>
            {
                ep.PrefetchCount          = opts.PrefetchCount;          // default 4
                ep.ConcurrentMessageLimit = opts.ConcurrentMessageLimit; // default 4
                ep.Durable                = true;
                ep.ConfigureConsumeTopology = false; // we declare topology manually

                ep.UseMessageRetry(r => r.Exponential(3,
                    TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)));

                ep.ConfigureConsumer<OperationRequestConsumer>(ctx);
            });
        }
    });
});
```

### `RouterOptions`

```csharp
public sealed class RouterOptions
{
    public const string Section = "Router";
    public int PrefetchCount          { get; init; } = 4;
    public int ConcurrentMessageLimit { get; init; } = 4;
    public int MessageTtlMs           { get; init; } = 600_000;
    public int ShutdownTimeoutSeconds { get; init; } = 30;
}
```

`PrefetchCount == ConcurrentMessageLimit` is intentional: the consumer never holds more
unacked messages than it can process concurrently, so RabbitMQ requeues promptly when the
worker restarts.

### Retry policy rationale

The retry (`Exponential(3, 1s, 10s)`) applies only to transient infrastructure faults
(e.g., Redis `ECONNRESET` mid-dispatch). It does NOT retry operation-level failures:
`OperationDispatcher` converts those to `ResponseStatus.Failed` and returns normally —
so from MassTransit's perspective the `Consume` call succeeded. After 3 retries the
message is dead-lettered. DLQ consumers are out of scope for Phase 6.

---

## §3 Health checks

Endpoint: `GET /healthz/live` → 200 always (process alive)  
Endpoint: `GET /healthz/ready` → 200 when all dependencies reachable

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self",        () => HealthCheckResult.Healthy(), ["live"])
    .AddNpgSql(pgConnStr,    name: "postgres", tags: ["ready"])
    .AddRedis(redisConnStr,  name: "redis",    tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

app.MapHealthChecks("/healthz/live",  new() { Predicate = r => r.Tags.Contains("live")  });
app.MapHealthChecks("/healthz/ready", new() { Predicate = r => r.Tags.Contains("ready") });
```

`RabbitMqHealthCheck` calls `IBusControl.CheckHealth()` (MassTransit built-in) and maps
the result to `HealthCheckResult`. No custom TCP probe needed.

Port: **5080** (configurable via `ASPNETCORE_URLS`; avoids conflict with future API gateway
on 5000/5001). Worker uses `WebApplication` (not `GenericHost`) so health endpoint is
built-in.

---

## §4 Graceful shutdown

MassTransit honors `IHostApplicationLifetime.ApplicationStopping` automatically when
registered via `AddMassTransit`. The shutdown sequence is:

1. `IHostApplicationLifetime.StopApplication()` fires (SIGTERM or dotnet stop).
2. MassTransit stops fetching new messages from all queues.
3. In-flight `Consume()` calls continue to completion (up to `RouterOptions.ShutdownTimeoutSeconds`, default 30 s).
4. `CancellationToken` in `ConsumeContext` is cancelled after the timeout — the dispatcher's
   deadline-linked CTS propagates cancellation into the handler, which returns `OPERATION_TIMEOUT`.
5. All remaining responses are published, then MassTransit disconnects from RabbitMQ.
6. `ProgressRingBufferAdapter` drain completes before the connection closes because
   `ProgressReporter.DisposeAsync()` awaits the channel drain synchronously within `DispatchAsync`.

No explicit shutdown code needed beyond `services.AddMassTransit` + standard host lifetime.

---

## §5 Test scenarios (tests/Router.Tests/)

All tests use fakes — no real RabbitMQ or Redis needed.

| # | Test name | What it asserts |
|---|-----------|-----------------|
| T1 | `Consumer_HappyPath_DispatchesAndPublishesResponse` | Dispatcher called once; `IPublishEndpoint.Publish` called once with `Status=Done` |
| T2 | `Consumer_DeadlineInPast_PublishesTimeoutResponse` | `TimeoutAtUnixMs` set to 0 (epoch); published response has `Status=Timeout, Code=DEADLINE_EXCEEDED` |
| T3 | `Consumer_DispatcherReturnsValidationError_PublishesFailed` | Dispatcher returns `Status=Failed`; response published as-is (consumer doesn't re-map errors) |
| T4 | `Consumer_CancellationDuringDispatch_PublishesTimeout` | Pass a pre-cancelled `CancellationToken`; dispatcher returns `Status=Timeout`; response published |
| T5 | `Consumer_WantsProgress_ProgressEventsRecordedInBuffer` | Use `RecordingProgressBuffer`; after consume, buffer has ≥1 event with correct `RequestId` |
| T6 | `Consumer_UnexpectedException_DoesNotSwallow` | Inject dispatcher stub that throws `InvalidOperationException`; assert exception propagates (MassTransit will retry/DLQ) |
| T7 | `DI_AllRouterDependencies_ResolveWithoutException` | Direct `ServiceCollection` with all faked infrastructure interfaces; `BuildServiceProvider(validateScopes: true)`; resolve `OperationRequestConsumer` — no `InvalidOperationException` |

**Fake strategy**: `IConsumer<OperationRequestMessage>` wraps a fake `OperationDispatcher`
constructed via the same pattern as `OperationDispatcherTests` (fake handler registry +
`FakeParamsValidator`). `IPublishEndpoint` is faked with a simple recording stub.

**T7 implementation pattern** (ServiceCollection, not WebApplicationBuilder):
```csharp
[Fact]
public void DI_AllRouterDependencies_ResolveWithoutException()
{
    var services = new ServiceCollection();

    // Infrastructure stubs — no real RabbitMQ/Redis/Postgres
    services.AddSingleton<IPublishEndpoint, RecordingPublishEndpoint>();
    services.AddSingleton<IProgressBuffer, RecordingProgressBuffer>();
    services.AddSingleton<IParamsValidator>(FakeParamsValidator.AlwaysValid());
    services.AddSingleton<OperationHandlerRegistry>(_ =>
    {
        var registry = new OperationHandlerRegistry();
        registry.Register(FakeOperationHandler.Success("test.op", default));
        return registry;
    });
    services.AddSingleton<OperationDispatcher>();

    // System under test
    services.AddScoped<OperationRequestConsumer>();

    var provider = services.BuildServiceProvider(validateScopes: true);
    using var scope = provider.CreateScope();

    // Assert: resolves without InvalidOperationException (missing registration / captive dependency)
    var consumer = scope.ServiceProvider.GetRequiredService<OperationRequestConsumer>();
    Assert.NotNull(consumer);
}
```

`validateScopes: true` catches singleton-consuming-scoped (captive dependency) at test time,
not at production runtime. Full `WebApplication` smoke test (with real MassTransit
configuration) moves to Phase 12 integration suite.

---

## §6 Logging conventions

All logs use structured properties. No interpolated strings in `Log*` calls.

| Level | Event | Properties |
|---|---|---|
| `Information` | Message received | `{Operation}`, `{RequestId}`, `{TenantId}`, `{Priority}` |
| `Information` | Dispatch complete | `{Operation}`, `{RequestId}`, `{Status}`, `{ElapsedMs}` |
| `Warning` | Timeout or deadline exceeded | `{RequestId}`, `{Code}` |
| `Warning` | Retry attempt | `{RequestId}`, `{AttemptNumber}`, `{ExceptionType}` |
| `Error` | Unhandled exception during dispatch | `{RequestId}`, full exception |
| `Debug` | Progress reporter drained | `{RequestId}`, `{EventCount}` |

Source context: `ReportingPlatform.Router.Consumers.OperationRequestConsumer`

ActivitySource: reuse `ActivitySources.Operations` (already in Telemetry) — start child span
`"operation.consume"` with tags `operation.name`, `tenant.id`, `request.id`. Traceparent
from `msg.Traceparent` is restored via `Activity.Current` before calling the dispatcher so
the full distributed trace spans submit → route → dispatch.

---

## §7 Open questions

| # | Question | Default if not answered before implementation |
|---|----------|----------------------------------------------|
| OQ1 | Should the Router also publish `OperationProgressMessage` to a `commands.progress` RabbitMQ queue for Phase 7, or does Phase 7 read progress exclusively from Redis (the Phase 5 design)? | Redis-only (no MassTransit progress publish). The `ProgressRingBuffer` is the source of truth. Phase 7 subscribes to Redis pub/sub `sse-progress:{requestId}` and streams to SSE clients. |
| OQ2 | Multiple Router worker instances (scale-out): are competing consumers across instances desirable for all priority queues? | Yes — standard RabbitMQ competing-consumer model. No per-instance affinity needed. |
| OQ3 | Should `PrefetchCount` be derived from `Environment.ProcessorCount` or kept as a fixed config value? | Fixed config (`RouterOptions.PrefetchCount = 4`) with documentation to tune per deployment. |
| OQ4 | DLQ consumer scope: defer `admin.dlq.list` / `admin.dlq.replay` to Phase 11. Phase 6 produces DLQ messages; consumption is an admin/ops function grouped with other admin operations in Phase 11. Until then: ops inspects DLQ via RabbitMQ Management UI directly. | **Approved — deferred to Phase 11.** |

---

## §8 Phase 6 ships when

1. `Services/Operation.Router.Worker/` builds clean (`0 warnings, 0 errors`, `TreatWarningsAsErrors=true`)
2. All 7 Router.Tests pass
3. `/healthz/live` and `/healthz/ready` endpoints respond correctly (verified with manual `curl` or integration test)
4. Graceful shutdown verified: worker receives SIGTERM, drains in-flight messages, exits cleanly
5. `DECISIONS.md` updated with OQ1 resolution
6. Commit: `feat(router): Phase 6 — Operation Router Worker`
