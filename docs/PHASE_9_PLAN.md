# PHASE_9_PLAN.md — External Provider SDK + Sample Providers
> Status: APPROVED
> Phase 8 commit: 98f4211
> Audience: Platform team — internal implementation plan

---

## Overview

Phase 9 delivers the **provider-side SDK** that wraps the gRPC protocol established in Phase 8, two runnable sample providers (one .NET, one Python), a unit test suite for SDK correctness, and a deferred integration test. The SDK is the primary integration surface for all future external teams — quality and ergonomics matter as much as correctness.

Deliverables:
- `Shared/ProviderSdk/` — .NET SDK (will eventually be published as a NuGet package)
- `samples/DotnetProviderSample/` — runnable .NET provider using ProviderSdk
- `samples/PythonProviderSample/` — runnable Python provider using raw gRPC
- `tests/ProviderSdk.Tests/` — unit tests for all SDK lifecycle features
- `docker-compose.providers.yml` — wires both samples into the platform dev stack

---

## §1 SDK API Design

### 1.1 Configuration shape

```csharp
services.AddProviderSdk(opts =>
{
    opts.ProviderId      = "ml-team-fraud";
    opts.ClientId        = "ml-team-fraud-c83hf";
    opts.ClientSecret    = config["Provider:ClientSecret"];   // from Vault / env
    opts.TokenEndpoint   = new Uri("https://platform/api/v1/providers/token");
    opts.BridgeEndpoint  = new Uri("https://provider-bridge.platform:5400");
    opts.Version         = "1.2.0";
    // Optional overrides (defaults shown):
    opts.MaxConcurrentRequests = 8;
    opts.HeartbeatInterval     = TimeSpan.FromSeconds(30);
    opts.TokenRefreshEarlyPct  = 0.80;   // refresh at 80% of expiresIn
    opts.MaxReconnectDelay     = TimeSpan.FromSeconds(30);
    opts.ReconnectJitterPct    = 0.10;
    opts.GrpcChannelOptions    = null;   // HttpClientHandler override, TLS certs, etc.
});
```

`ProviderSdkOptions` uses a fluent validator that throws `ArgumentException` on startup if required fields are blank, endpoints are not HTTPS in production, or `MaxConcurrentRequests < 1`.

### 1.2 Operation handler registration

Two styles — class-based (DI-friendly, testable) and delegate (inline for simple cases):

```csharp
// Class-based — recommended for complex handlers
builder.Services
    .AddProviderSdk(opts => { ... })
    .Handle<FraudScoreParams, FraudScoreResult>("ml.fraud.score")
    .Handle<BatchScoreParams, BatchScoreResult>("ml.fraud.batchScore");

// Delegate-based — acceptable for trivial operations
builder.Services
    .AddProviderSdk(opts => { ... })
    .Handle("ml.simple.ping", async (req, progress, ct) =>
        OperationResult.Success(new { pong = true }));
```

Class-based handlers registered with `.Handle<P, R>(operation)` require a corresponding DI registration:
```csharp
builder.Services.AddScoped<FraudScoreHandler>();
```

The SDK resolves the handler from `IServiceProvider` per request, so handlers may inject scoped dependencies (DB contexts, HTTP clients, etc.).

### 1.3 `IOperationHandler<TParams, TResult>` interface

```csharp
namespace ReportingPlatform.ProviderSdk;

/// <summary>
/// Implement this interface to handle a single registered operation type.
/// TParams and TResult must be JSON-serializable (source-gen context recommended).
/// </summary>
public interface IOperationHandler<TParams, TResult>
    where TParams : class
    where TResult : class
{
    Task<OperationResult<TResult>> HandleAsync(
        OperationContext<TParams> context,
        CancellationToken ct);
}

/// <summary>Per-request context passed to each handler invocation.</summary>
public sealed record OperationContext<TParams>
{
    public required string          RequestId      { get; init; }
    public required string          Operation      { get; init; }
    public required TParams         Params         { get; init; }
    public required string          TenantId       { get; init; }
    public required string          UserId         { get; init; }
    public required DateTimeOffset  Deadline       { get; init; }
    public required bool            WantsProgress  { get; init; }
    public required string          Traceparent    { get; init; }
    public required string          CorrelationId  { get; init; }
    /// <summary>Report progress (1-99). No-op if WantsProgress=false.</summary>
    public required ProgressReporter Progress      { get; init; }
}

public sealed class ProgressReporter
{
    // Internal. SDK injects implementation; handlers call it.
    public Task ReportAsync(int percent, string message, CancellationToken ct = default);
}

/// <summary>Return from HandleAsync.</summary>
public sealed record OperationResult<TResult>
{
    public static OperationResult<TResult> Success(TResult payload);
    public static OperationResult<TResult> Failure(string code, string message, string? detailsJson = null);
    public static OperationResult<TResult> Cancelled();
}
```

### 1.4 Lifecycle callbacks

```csharp
builder.Services
    .AddProviderSdk(opts => { ... })
    .OnConnected((sessionId, welcome) => logger.LogInformation("Connected session={}", sessionId))
    .OnDisconnected((reason) => logger.LogWarning("Disconnected reason={}", reason))
    .OnCredentialsRevoked(() =>
    {
        // MUST NOT reconnect. Alert ops team.
        logger.LogCritical("Credentials revoked — operator intervention required");
        // SDK will NOT reconnect automatically after this fires.
    })
    .OnReconnecting((attempt, delay) => metrics.IncrementReconnect())
    .Handle<...>(...);
```

All callbacks are `Func<..., Task>` (async-safe). Sync variants auto-wrapped.

### 1.5 ProviderSdkBuilder (fluent chain)

`AddProviderSdk(...)` returns `IProviderSdkBuilder` which exposes `Handle`, `OnConnected`, `OnDisconnected`, `OnCredentialsRevoked`, `OnReconnecting`. Each returns `IProviderSdkBuilder` for chaining. Terminal (implicitly) — `IProviderSdkBuilder` registers the `ProviderClient` hosted service into DI.

---

## §2 Project Structure

```
Shared/
└── ProviderSdk/
    ├── ProviderSdk.csproj
    ├── GlobalUsings.cs
    ├── ProviderSdkOptions.cs          // Configuration shape + validation
    ├── IProviderSdkBuilder.cs         // Fluent builder interface
    ├── ProviderSdkBuilder.cs          // Builder implementation
    ├── ProviderSdkExtensions.cs       // AddProviderSdk() extension method
    ├── IOperationHandler.cs           // IOperationHandler<TParams, TResult>
    ├── OperationContext.cs            // OperationContext<TParams>, ProgressReporter
    ├── OperationResult.cs             // OperationResult<TResult>
    ├── Internal/
    │   ├── TokenManager.cs            // JWT acquire + 80% TTL background refresh
    │   ├── ConnectionManager.cs       // Reconnection loop, backoff, state machine
    │   ├── HandlerRegistry.cs         // Maps operation name → handler factory
    │   ├── RequestDispatcher.cs       // Routes OperationRequest → handler, manages concurrency
    │   ├── HeartbeatSender.cs         // Sends Heartbeat every heartbeatIntervalSeconds
    │   ├── CancellationTracker.cs     // requestId → CancellationTokenSource for Cancel messages
    │   ├── ProgressReporterImpl.cs    // Sends OperationResponseChunk(Progress) over stream
    │   └── SdkJsonContext.cs          // [JsonSerializable] source-gen context
    └── Models/
        └── TokenResponse.cs           // Deserialized token endpoint response

samples/
├── DotnetProviderSample/
│   ├── DotnetProviderSample.csproj
│   ├── Program.cs                     // DI wiring, host setup
│   ├── Handlers/
│   │   ├── FraudScoreHandler.cs       // ml.fraud.score — random 0.0-1.0 score
│   │   └── BatchScoreHandler.cs       // ml.fraud.batchScore — progress per chunk
│   ├── SelfRegistration/
│   │   └── ProviderSelfRegistrar.cs   // IHostedService: registers if not already exists
│   ├── appsettings.json
│   ├── Dockerfile
│   └── README.md
│
└── PythonProviderSample/
    ├── main.py                        // Entry point + reconnection loop
    ├── token_manager.py               // JWT acquire + 80% refresh
    ├── provider_client.py             // gRPC channel, Hello/Welcome, message loop
    ├── handlers/
    │   └── forecast_timeseries.py     // forecast.timeseries — mock ML prediction + progress
    ├── self_register.py               // REST self-registration on startup
    ├── generate_proto.sh              // grpc_tools.protoc invocation
    ├── requirements.txt
    ├── Dockerfile
    └── README.md

tests/
└── ProviderSdk.Tests/
    ├── ProviderSdk.Tests.csproj
    ├── GlobalUsings.cs
    ├── Helpers/
    │   ├── FakeTokenServer.cs         // WireMock-style HTTP stub for token endpoint
    │   ├── FakeBridgeServer.cs        // In-process gRPC server stub
    │   └── TestConnectionManager.cs   // Wraps ConnectionManager with controllable stream
    ├── TokenManagerTests.cs           // SD1-SD4
    ├── ReconnectionTests.cs           // SD5-SD8
    ├── HandlerDispatchTests.cs        // SD9-SD12
    └── IntegrationTests.cs            // SI1 (skipped — Testcontainers, enable Phase 12)

docker-compose.providers.yml          // Overlay for both samples
```

### Csproj highlights

**`Shared/ProviderSdk/ProviderSdk.csproj`**:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <!-- Phase 11: publish as NuGet -->
    <PackageId>ReportingPlatform.ProviderSdk</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Grpc.Net.Client" Version="2.63.0" />
    <PackageReference Include="Google.Protobuf" Version="3.29.3" />
    <PackageReference Include="Grpc.Tools" Version="2.63.0" PrivateAssets="All" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" />
    <PackageReference Include="OpenTelemetry" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Api" Version="1.10.0" />
  </ItemGroup>
  <ItemGroup>
    <Protobuf Include="..\..\proto\provider.proto" GrpcServices="Client" />
  </ItemGroup>
</Project>
```

**`tests/ProviderSdk.Tests/ProviderSdk.Tests.csproj`**:
```xml
<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="9.0.0" />
<PackageReference Include="WireMock.Net" Version="1.6.8" />
<PackageReference Include="Grpc.AspNetCore" Version="2.63.0" />
```

---

## §3 JWT Lifecycle State Machine

```
          ┌─────────────────────────────────────────────────────────────┐
          │                     ProviderClient                          │
          │                                                             │
          │   ┌──────────────┐    token OK    ┌──────────────────┐     │
  Start──►│   │ AcquiringJwt ├───────────────►│  OpeningChannel  │     │
          │   └──────┬───────┘                └────────┬─────────┘     │
          │          │ 401 invalid_client               │ gRPC OK       │
          │          │ (NOT credentials_revoked         │               │
          │          │  — that happens on stream close) │               │
          │          ▼                        ┌─────────▼─────────┐     │
          │   ┌──────────────┐                │  SendingHello     │     │
          │   │   Stopped    │◄──────────────┤  AwaitingWelcome  │     │
          │   └──────────────┘  revoked       └─────────┬─────────┘     │
          │          ▲                                   │ Welcome       │
          │          │ OnStop()                          │               │
          │          │                        ┌──────────▼──────────┐   │
          │          │                        │        Active        │   │
          │          │                        │  (serving requests)  │   │
          │          │                        └──────┬──────┬───────┘   │
          │          │                               │      │            │
          │          │            RefreshAuthRequired│      │stream err  │
          │          │            or 80% TTL timer   │      │/ net err   │
          │          │                        ┌──────▼──┐ ┌─▼──────────┐│
          │          │                        │Refreshing│ │Reconnecting││
          │          │                        │(drain in-│ │(backoff)   ││
          │          │                        │ flight)  │ └────────────┘│
          │          │                        └──────────┘               │
          └─────────────────────────────────────────────────────────────┘
```

### State transitions (precise)

| From | Event | To | Action |
|---|---|---|---|
| `AcquiringJwt` | 200 OK + accessToken | `OpeningChannel` | Cache token, start 80% refresh timer |
| `AcquiringJwt` | 401 `invalid_client` | `AcquiringJwt` | Backoff, retry (could be network blip) |
| `AcquiringJwt` | Non-401 error | `AcquiringJwt` | Backoff, retry |
| `AcquiringJwt` | 5 consecutive 401s | `Stopped` | Fire `OnCredentialsRevoked` |
| `OpeningChannel` | Channel open OK | `SendingHello` | Attach JWT header, send `Hello` |
| `OpeningChannel` | Channel error | `AcquiringJwt` | Backoff; fresh token on re-acquire |
| `SendingHello` | `Welcome` received | `Active` | Start `HeartbeatSender`, `RequestDispatcher` |
| `SendingHello` | `UNAUTHENTICATED` from Bridge | `AcquiringJwt` | Token may be stale; re-acquire |
| `SendingHello` | `INVALID_ARGUMENT` from Bridge | `Stopped` | Config error; log, do not retry |
| `SendingHello` | Timeout (5s) | `AcquiringJwt` | Backoff, reconnect |
| `Active` | `RefreshAuthRequired` | `Refreshing` | Stop accepting new dispatches, let in-flight finish |
| `Active` | 80% TTL timer fires | `Refreshing` | Same as above |
| `Active` | Stream error / network drop | `Reconnecting` | NACK in-flight (requeue), backoff |
| `Active` | `Disconnect(credentials_revoked)` | `Stopped` | Fire `OnCredentialsRevoked`, no retry |
| `Active` | `Disconnect(server_shutdown/idle_timeout)` | `Reconnecting` | Backoff |
| `Active` | `Disconnect(provider_suspended)` | `Reconnecting` | Long backoff (60s fixed), log warning |
| `Refreshing` | In-flight drained (<30s) | `AcquiringJwt` | Close old channel, get fresh token |
| `Refreshing` | 30s elapsed, handlers still running | `Refreshing` | CancelAll() via CancellationTracker |
| `Refreshing` | 35s elapsed (force-close) | `AcquiringJwt` | Tear down stream; remaining handlers get Terminal(CANCELLED) |
| `Reconnecting` | Backoff elapsed | `AcquiringJwt` | Always re-acquire token on reconnect |
| `Stopped` | — | — | Terminal; `IHostedService.StopAsync` completes |

### 80% refresh timer

`TokenManager` stores `issuedAt + expiresIn * 0.80` as the refresh deadline. A `PeriodicTimer` or `Task.Delay` loop fires at or after this deadline. The refresh is non-disruptive: a new JWT is acquired in the background, then `ConnectionManager.TriggerRefresh()` is called, which moves to `Refreshing` state to drain in-flight work before opening the new stream.

### Token caching invariant

The current token is cached in `TokenManager` as `(string Token, DateTimeOffset ExpiresAt)`. `GetTokenAsync()` returns the cached token if `ExpiresAt - UtcNow > TimeSpan.FromSeconds(90)` (90s buffer), otherwise acquires a fresh one. This prevents the case where a reconnect fires 1 second before expiry and the new stream gets a token with <1s remaining.

---

## §4 Reconnection Strategy

### Backoff sequence (with ±10% jitter)

```
Attempt 1:  1s  × (0.90 – 1.10 random)  =  0.9s – 1.1s
Attempt 2:  2s  × jitter
Attempt 3:  4s  × jitter
Attempt 4:  8s  × jitter
Attempt 5:  16s × jitter
Attempt 6+: 30s × jitter  (cap)
```

`ConnectionManager` resets the attempt counter to 0 on every successful `Welcome` receipt. Jitter uses `Random.Shared.NextDouble() * 0.20 + 0.90` (uniform ±10%).

> **OQ-P9-A — Backoff cap discrepancy**: `PROVIDER_PROTOCOL.md §13.1` says max 60s; Phase 9 scope says max 30s. Plan uses **30s** per Phase 9 prompt. Needs confirmation before implementation.

### `credentials_revoked` — no retry

When `Disconnect.reason == "credentials_revoked"` arrives, `ConnectionManager` transitions to `Stopped` unconditionally. `OnCredentialsRevoked` fires. No further token requests or connection attempts are made. Application must be restarted with new credentials after the rotation procedure (see PROVIDER_PROTOCOL.md §3.8).

### `INVALID_ARGUMENT` from Bridge — no retry

If Bridge rejects `Hello` with `INVALID_ARGUMENT` (unregistered operation in `supportedOperations`), this is a configuration error that won't heal on retry. `ConnectionManager` transitions to `Stopped`, logs the error with the full gRPC status message, and throws `ProviderSdkConfigurationException` from `StartAsync`.

---

## §5 Python Sample Structure

### Runtime requirements

- Python 3.11+ (matches `grpcio 1.62+` wheel availability)
- `grpcio 1.62.2`, `grpcio-tools 1.62.2` — gRPC runtime + protoc plugin
- `requests 2.31.0` — REST token endpoint + self-registration
- `opentelemetry-sdk 1.24.0`, `opentelemetry-exporter-otlp 1.24.0` — tracing
- No `asyncio` — use synchronous gRPC for simplicity (thread-per-request model)

### Proto generation (not committed)

```bash
# generate_proto.sh
python -m grpc_tools.protoc \
  -I ../../proto \
  --python_out=. \
  --grpc_python_out=. \
  ../../proto/provider.proto
```

Run once after clone. `provider_pb2.py` and `provider_pb2_grpc.py` added to `.gitignore`.

### File map

```
samples/PythonProviderSample/
├── main.py                  # Configure + start; calls self_register, then provider_client.run()
├── config.py                # Reads env vars: PROVIDER_ID, CLIENT_ID, CLIENT_SECRET,
│                            #   TOKEN_ENDPOINT, BRIDGE_ENDPOINT
├── token_manager.py         # fetch_token(); refresh_if_needed() checks 80% threshold
├── provider_client.py       # Main loop: acquire token → open channel → Hello → Welcome →
│                            #   serve_loop → RefreshAuthRequired / disconnect → reconnect
├── handler_registry.py      # Dict[str, Callable] — maps operation to handler function
├── handlers/
│   └── forecast_timeseries.py   # Returns mock sinusoidal prediction with 5 progress chunks
├── self_register.py         # GET /api/v1/admin/providers/{id} → 404? → POST to register
├── generate_proto.sh
├── requirements.txt
├── Dockerfile
└── README.md
```

### JWT lifecycle (Python — manual, per PROVIDER_PROTOCOL.md §16.2)

```python
# token_manager.py
import time, requests

class TokenManager:
    def __init__(self, config):
        self._config = config
        self._token = None
        self._expires_at = 0  # Unix seconds

    def get_token(self):
        if self._token and time.time() < self._expires_at - 90:
            return self._token
        return self._fetch()

    def should_refresh(self):
        return self._token and time.time() > self._expires_at * 0.80  # 80% of absolute expiry

    def _fetch(self):
        r = requests.post(self._config.token_endpoint, json={
            "clientId": self._config.client_id,
            "clientSecret": self._config.client_secret,
            "grantType": "client_credentials"
        }, timeout=10)
        if r.status_code == 401:
            raise CredentialsRevokedException()
        r.raise_for_status()
        data = r.json()
        self._token = data["accessToken"]
        self._expires_at = time.time() + data["expiresIn"]
        return self._token
```

> **Note**: The Python 80% check uses `time.time() > self._expires_at * 0.80` — this computes 80% of the *absolute* Unix epoch timestamp, not 80% of the remaining lifetime. The correct formula is `time.time() > issued_at + (expires_in * 0.80)`. This is a subtle bug that will be flagged in the README and corrected in the implementation — noted here as a design decision.

Correct formula used in implementation:
```python
self._issued_at = time.time()
self._expires_in = data["expiresIn"]
# Refresh at 80% of lifetime:
def should_refresh(self):
    return time.time() > self._issued_at + self._expires_in * 0.80
```

### Reconnection (Python)

No exponential backoff library — implement manually:

```python
BACKOFF_SEQUENCE = [1, 2, 4, 8, 16, 30]  # seconds
def get_backoff(attempt: int) -> float:
    base = BACKOFF_SEQUENCE[min(attempt, len(BACKOFF_SEQUENCE) - 1)]
    jitter = base * random.uniform(-0.10, 0.10)
    return base + jitter
```

### `forecast.timeseries` operation

Input params (JSON):
```json
{ "seriesId": "string", "horizon": 12, "granularity": "monthly" }
```

Output payload (JSON):
```json
{
  "seriesId": "...",
  "predictions": [{ "period": 1, "value": 142.3, "lower": 120.1, "upper": 164.5 }],
  "modelVersion": "mock-v1",
  "confidence": 0.87
}
```

Progress: sends 5 progress events at 20%, 40%, 60%, 80%, 99% with `Task.Delay(horizon * 10ms)` simulation. Respects `wants_progress` flag.

### Traceparent propagation (Python)

```python
from opentelemetry import trace
from opentelemetry.propagate import inject, extract

# Extract from OperationRequest.traceparent:
ctx = extract({"traceparent": request.traceparent})
with tracer.start_as_current_span("forecast.timeseries", context=ctx) as span:
    span.set_attribute("tenant.id", request.tenant_id)
    span.set_attribute("request.id", request.request_id)
    # ... run prediction
```

### Self-registration (Python)

`self_register.py` calls `GET /api/v1/admin/providers/{providerId}` at startup. If 404, calls `POST /api/v1/admin/providers` with hardcoded registration payload and saves the returned `clientSecret` to a local file (dev only — in prod, credentials pre-exist in Vault). If `clientSecret` file exists, skips registration.

---

## §6 .NET Sample Structure

### Operation: `ml.fraud.score`

Input:
```json
{ "transactionId": "string", "amount": 0.0, "merchantCategory": "string", "features": {} }
```

Output:
```json
{ "transactionId": "string", "score": 0.73, "riskBand": "HIGH", "modelVersion": "mock-v1" }
```

Handler: `FraudScoreHandler` — `score = Random.Shared.NextDouble()`, `riskBand` derived from thresholds (< 0.3 = LOW, < 0.7 = MEDIUM, ≥ 0.7 = HIGH). No progress (single-shot). ~2ms mock latency (`Task.Delay(2)`).

### Operation: `ml.fraud.batchScore`

Input:
```json
{ "transactions": [{ "transactionId": "...", "amount": 0.0, "merchantCategory": "...", "features": {} }] }
```

Output:
```json
{ "results": [{ "transactionId": "...", "score": 0.0, "riskBand": "..." }], "processed": 10, "modelVersion": "mock-v1" }
```

Handler: `BatchScoreHandler` — processes transactions in chunks of 10. Sends progress after each chunk (`percent = chunksComplete / totalChunks * 99`). Reports `message = "Scored chunk {i}/{total}"`.

### `Program.cs` structure

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProviderSdk(opts => {
    opts.ProviderId     = builder.Configuration["Provider:ProviderId"]!;
    opts.ClientId       = builder.Configuration["Provider:ClientId"]!;
    opts.ClientSecret   = builder.Configuration["Provider:ClientSecret"]!;
    opts.TokenEndpoint  = new Uri(builder.Configuration["Provider:TokenEndpoint"]!);
    opts.BridgeEndpoint = new Uri(builder.Configuration["Provider:BridgeEndpoint"]!);
    opts.Version        = "1.0.0";
})
.Handle<FraudScoreParams, FraudScoreResult>("ml.fraud.score")
.Handle<BatchScoreParams, BatchScoreResult>("ml.fraud.batchScore")
.OnCredentialsRevoked(() => {
    Log.Fatal("Credentials revoked — stopping host");
    hostLifetime.StopApplication();
});

builder.Services.AddScoped<FraudScoreHandler>();
builder.Services.AddScoped<BatchScoreHandler>();
builder.Services.AddHostedService<ProviderSelfRegistrar>();

// Health endpoint (PROVIDER_PROTOCOL.md §10.3)
builder.Services.AddHealthChecks();
var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

### `ProviderSelfRegistrar`

`IHostedService` that runs on startup:
1. `GET /api/v1/admin/providers/{providerId}` — if 200, skip (already registered).
2. If 404: `POST /api/v1/admin/providers` with hardcoded registration payload.
3. Saves returned `clientSecret` to `appsettings.Development.json` (dev only). In production, credentials pre-exist and this service is a no-op.
4. Failure is non-fatal (logs warning, continues). The SDK will fail to authenticate on its own if credentials don't exist.

---

## §7 Test Scenarios

**File**: `tests/ProviderSdk.Tests/`

### `TokenManagerTests.cs` — SD1–SD4

| ID | Scenario | Key assertion |
|---|---|---|
| SD1 | Valid credentials → `GetTokenAsync` returns token | `Token != null`, `ExpiresAt` set correctly from `expiresIn` |
| SD2 | 80% TTL elapsed → `ShouldRefresh()` returns true | Timer at `issuedAt + expiresIn * 0.80` |
| SD3 | Token with >90s remaining → `GetTokenAsync` returns cached | HTTP stub NOT called second time (call count = 1) |
| SD4 | 5 consecutive 401s from token endpoint → `CredentialsRevokedException` thrown | Exception type + HTTP stub call count = 5 |

### `ReconnectionTests.cs` — SD5–SD8

| ID | Scenario | Key assertion |
|---|---|---|
| SD5 | Bridge stream closes with `UNAVAILABLE` → SDK reconnects after backoff | `OnReconnecting` fires; 2nd `Hello` sent after ≥ 1s |
| SD6 | Backoff sequence correct (mock `IDelay`) | Delays sequence: 1s, 2s, 4s, 8s, 16s, 30s, 30s |
| SD7 | `Disconnect(credentials_revoked)` → `OnCredentialsRevoked` fires; no reconnect | Reconnect attempt count = 0 after revocation |
| SD8 | `RefreshAuthRequired` → in-flight request completes before reconnect | Handler result received before new `Hello` sent |

### `HandlerDispatchTests.cs` — SD9–SD12

| ID | Scenario | Key assertion |
|---|---|---|
| SD9 | `OperationRequest` dispatched to registered handler → `Terminal(DONE)` sent | `Terminal.status == DONE`, `payload_json` non-empty |
| SD10 | Handler throws `OperationCanceledException` → `Terminal(CANCELLED)` sent | `Terminal.status == CANCELLED` |
| SD11 | `Cancel` message received mid-handler → `CancellationToken` is cancelled | Handler's CT is cancelled; `Terminal(CANCELLED)` sent |
| SD12 | `traceparent` from request propagated to `Activity.TraceId` inside handler | `Activity.Current?.TraceId.ToString()` matches W3C trace ID from request |

### `ReconnectionTests.cs` — SD8b (new — Refreshing force-close)

| ID | Scenario | Key assertions |
|---|---|---|
| SD8b | Handler runs >35s; `Refreshing` timeout force-closes stream | `Terminal(CANCELLED)` sent; new `Hello` follows force-close; no deadlock; test completes within 10s (mock delays instant) |

**Total SDK unit tests: 13** (SD1–SD8, SD8b, SD9–SD12)

### `IntegrationTests.cs` — SI1 (skipped until Phase 12)

```csharp
[Fact(Skip = "Requires Docker (Testcontainers) — enable in Phase 12")]
public async Task SI1_EndToEnd_ProviderServesRequest_ResponseOnSignalR() { ... }
```

Design (for reference):
1. Testcontainers spins up: Postgres, RabbitMQ, Redis, `Request.Api`, `Provider.Bridge`
2. Register provider via admin REST → get credentials
3. Start `ProviderClient` with real gRPC against containerised Bridge
4. Submit request via `POST /api/v1/requests`
5. Connect to SignalR hub
6. Assert `completed` event arrives within 5s with correct payload
7. Assert `TBIntegration_RealBCrypt` path is exercised (log check or counter)

---

## §8 Docker Compose Wiring

**`docker-compose.providers.yml`** (overlay — `docker compose -f docker-compose.yml -f docker-compose.providers.yml up`):

```yaml
services:
  dotnet-provider-sample:
    build: samples/DotnetProviderSample
    environment:
      Provider__ProviderId:     "ml-team-fraud"
      Provider__ClientId:       "ml-team-fraud-c83hf"
      Provider__ClientSecret:   "${ML_FRAUD_CLIENT_SECRET}"
      Provider__TokenEndpoint:  "http://request-api:8080/api/v1/providers/token"
      Provider__BridgeEndpoint: "http://provider-bridge:5400"
      ASPNETCORE_ENVIRONMENT:   "Development"
    depends_on:
      - request-api
      - provider-bridge
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 10s

  python-provider-sample:
    build: samples/PythonProviderSample
    environment:
      PROVIDER_ID:       "forecast-python"
      CLIENT_ID:         "forecast-python-x4k9f"
      CLIENT_SECRET:     "${FORECAST_CLIENT_SECRET}"
      TOKEN_ENDPOINT:    "http://request-api:8080/api/v1/providers/token"
      BRIDGE_ENDPOINT:   "provider-bridge:5400"
      OTEL_EXPORTER_OTLP_ENDPOINT: "http://otel-collector:4317"
    depends_on:
      - request-api
      - provider-bridge
```

> `ASPNETCORE_ENVIRONMENT: Development` disables TLS on the Bridge gRPC channel in both samples (dev mode uses plain HTTP/2 on port 5400).

---

## §9 Open Questions

| ID | Question | Default (if no response) |
|---|---|---|
| OQ-P9-A | Backoff cap: Phase 9 prompt says max 30s; PROVIDER_PROTOCOL.md §13.1 says max 60s. Which is authoritative? | **RESOLVED: 30s** — Phase 9 prompt wins. PROVIDER_PROTOCOL.md §13.1 updated to 30s in Phase 9 commit (single source of truth). |
| OQ-P9-B | Should `AddProviderSdk` automatically register an `ActivitySource` into the SDK, or does the caller pass in their own `ActivitySource`? | **RESOLVED: SDK-owned** — `ActivitySource("ReportingPlatform.ProviderSdk")`. Caller adds `.AddSource("ReportingPlatform.ProviderSdk")` to their OTEL builder. |
| OQ-P9-C | Python self-registration: fail fast or degraded? | **RESOLVED: Fail fast** — log `CRITICAL`, `sys.exit(1)`. |
| OQ-P9-D | `SemaphoreSlim` in SDK in addition to RabbitMQ prefetch? | **RESOLVED: Yes** — belt-and-suspenders. `SemaphoreSlim(MaxConcurrentRequests)` in `RequestDispatcher`. |
| OQ-P9-E | Integration test (SI1) schema migrations — Flyway or raw SQL? | **RESOLVED: Raw SQL** via Npgsql in test `GlobalSetup`. No Flyway container dependency. |
| OQ-P9-F | Python tenantId isolation — live demo or README note? | **RESOLVED: README note only**. Mock handler has no persistence to isolate. |

---

## §10 Implementation Order

1. `Shared/ProviderSdk/` — core SDK (all `Internal/` classes + public surface)
2. `tests/ProviderSdk.Tests/` — SD1–SD12 passing before samples
3. `samples/DotnetProviderSample/` — uses ProviderSdk; verifies API ergonomics end-to-end
4. `samples/PythonProviderSample/` — independent; raw gRPC
5. `docker-compose.providers.yml`
6. Build + test + verify:
   - `dotnet build` 0 warnings / 0 errors: ProviderSdk, DotnetProviderSample, ProviderSdk.Tests
   - `dotnet test tests/ProviderSdk.Tests/` → 12+ tests passing (SD1–SD12)
   - Python: `pip install -r requirements.txt && python generate_proto.sh && python main.py --selfcheck` (dry-run mode, no live platform)
   - SI1 skipped but present

---

## §11 Approved Patches

### Patch 1 — JWT state machine edge cases (expanded `AcquiringJwt` transitions + `Refreshing` timeout)

#### `AcquiringJwt` — expanded HTTP response handling

| HTTP response | Action |
|---|---|
| `200 OK` + `accessToken` | Cache token → `OpeningChannel` |
| `400 Bad Request` | **`Stopped` + throw `ProviderSdkConfigurationException`** — config error (bad body, wrong grant_type), no retry |
| `401 invalid_client` | Backoff; increment consecutive-401 counter |
| `401 invalid_client` (5th consecutive) | **`Stopped` + fire `OnCredentialsRevoked`** |
| `429 Too Many Requests` | Honor `Retry-After` header (seconds or HTTP-date); default 60s if header absent |
| `5xx Server Error` | Exponential backoff (same sequence as reconnect) |
| Network/timeout error | Exponential backoff |

The consecutive-401 counter resets to 0 on any non-401 success. This means a transient network error or 500 in between 401s does not carry the counter forward.

#### `Refreshing` state — 35s worst-case timeout

When the `Refreshing` state is entered (via `RefreshAuthRequired` message or 80% TTL timer):

1. **T+0s**: Stop accepting new dispatches (`RequestDispatcher.HoldNew = true`). Existing in-flight requests continue.
2. **T+30s**: If any handlers are still running, cancel their `CancellationToken` via `CancellationTracker.CancelAll()`. Handlers that observe cancellation send `Terminal(CANCELLED)`.
3. **T+35s**: Force-close the gRPC stream regardless. Any unfinished handlers that did NOT observe cancellation get a `Terminal(CANCELLED)` injected by the dispatcher. Transition to `AcquiringJwt`.
4. **In-flight drain succeeds before T+30s**: Transition to `AcquiringJwt` immediately.

> The 5s gap (30s → 35s) gives handlers that observe cancellation time to write their `Terminal(CANCELLED)` before the stream is torn down.

### Patch 2 — Explicit operation JSON Schemas for self-registration

Schemas are submitted with `POST /api/v1/admin/operations` during self-registration. They enable Phase 12 SI1 integration test payload conformance validation.

#### §5.5 — `forecast.timeseries` schema (Python sample)

**paramsSchema**:
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["seriesId", "horizon"],
  "properties": {
    "seriesId":    { "type": "string", "minLength": 1, "maxLength": 128 },
    "horizon":     { "type": "integer", "minimum": 1, "maximum": 120 },
    "granularity": { "type": "string", "enum": ["daily", "weekly", "monthly"], "default": "monthly" }
  },
  "additionalProperties": false
}
```

**payloadSchema**:
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["seriesId", "predictions", "modelVersion", "confidence"],
  "properties": {
    "seriesId": { "type": "string" },
    "predictions": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["period", "value", "lower", "upper"],
        "properties": {
          "period": { "type": "integer", "minimum": 1 },
          "value":  { "type": "number" },
          "lower":  { "type": "number" },
          "upper":  { "type": "number" }
        }
      }
    },
    "modelVersion": { "type": "string" },
    "confidence":   { "type": "number", "minimum": 0.0, "maximum": 1.0 }
  },
  "additionalProperties": false
}
```

#### §6.3 — `ml.fraud.score` and `ml.fraud.batchScore` schemas (.NET sample)

**`ml.fraud.score` paramsSchema**:
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["transactionId", "amount"],
  "properties": {
    "transactionId":      { "type": "string", "minLength": 1, "maxLength": 128 },
    "amount":             { "type": "number", "minimum": 0 },
    "merchantCategory":   { "type": "string", "maxLength": 64 },
    "features":           { "type": "object", "additionalProperties": true }
  },
  "additionalProperties": false
}
```

**`ml.fraud.score` payloadSchema**:
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["transactionId", "score", "riskBand", "modelVersion"],
  "properties": {
    "transactionId": { "type": "string" },
    "score":         { "type": "number", "minimum": 0.0, "maximum": 1.0 },
    "riskBand":      { "type": "string", "enum": ["LOW", "MEDIUM", "HIGH"] },
    "modelVersion":  { "type": "string" }
  },
  "additionalProperties": false
}
```

**`ml.fraud.batchScore` paramsSchema**: array of transactions (same shape as `ml.fraud.score` params, wrapped in `transactions: []`).

**`ml.fraud.batchScore` payloadSchema**: `results: []` (array of fraud score payloads) + `processed: integer` + `modelVersion: string`.

### Patch 3 — SD8 timing assertions + SD8b (Refreshing timeout forced close)

#### SD8 (refined) — `RefreshAuthRequired` → in-flight completes before reconnect

Handler is injected with a `TaskCompletionSource` so the test controls completion timing. Explicit assertions:
- Handler result (`Terminal(DONE)`) arrives **before** the new `Hello` is sent.
- New `Hello` sent **within 500ms** after `RefreshAuthRequired` received (assuming handler completes within that window, i.e., TCS.SetResult() called at T+50ms).
- Token endpoint called **twice** total (once on initial connect, once on refresh).

#### SD8b (new) — Long-running handler (35s) exceeds Refreshing timeout

Uses injected `IDelay` (instant mode) and a handler that hangs until CT is cancelled. Steps:
1. Stream sends `RefreshAuthRequired`.
2. `ConnectionManager` enters `Refreshing`.
3. Mock timer advances to T+30s → `CancellationTracker.CancelAll()` fires → handler's CT is cancelled.
4. Handler observes `OperationCanceledException`, returns `OperationResult.Cancelled()`.
5. `Terminal(CANCELLED)` sent on stream.
6. Mock timer advances to T+35s → old stream force-closed.
7. New `Hello` sent (fresh token acquired).

Assertions: `Terminal.status == CANCELLED`, `Hello` count == 2, no deadlock within 10s test timeout.

### Patch 4 — Docker compose secret handling note

#### §8.1 — Secret handling — dev vs production

The `docker-compose.providers.yml` uses environment variables for `CLIENT_SECRET`. This is **development-only**. Each sample's `README.md` MUST include:

> **Production secret delivery**: Do not pass `CLIENT_SECRET` as a plain environment variable in production. Use one of:
> - **Kubernetes**: `secretKeyRef` in pod spec
> - **Docker Swarm**: `docker secret create` + `secrets:` in compose
> - **HashiCorp Vault**: Vault agent sidecar injector
> - **AWS/GCP/Azure**: Secrets Manager with init container or SDK
>
> Never store credentials in `.env` files committed to version control, Docker images, or CI logs.

The `docker-compose.providers.yml` comment block references this note. A `.env.example` file is provided with placeholder values (not real secrets) so operators know which variables to set.

---

## Estimate

| Area | Effort |
|---|---|
| ProviderSdk core (TokenManager, ConnectionManager, Dispatcher, etc.) | 1.5 days |
| ProviderSdk.Tests (SD1–SD12) | 0.5 days |
| DotnetProviderSample | 0.5 days |
| PythonProviderSample | 0.75 days |
| Docker compose + README | 0.25 days |
| **Total** | **~3.5 days** |

SDK quality is the critical path — samples and tests follow naturally once the SDK surface is locked.
