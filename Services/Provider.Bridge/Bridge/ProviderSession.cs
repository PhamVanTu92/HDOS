using ReportingPlatform.Bridge.Consumers;
using ReportingPlatform.Bridge.Resilience;

namespace ReportingPlatform.Bridge.Bridge;

public sealed class ProviderSession
{
    public string SessionId  { get; }
    public string ProviderId { get; }

    private readonly IAsyncStreamReader<FromProvider>    _fromProvider;
    private readonly IServerStreamWriter<ToProvider>     _toProvider;
    private readonly ServerCallContext                   _callContext;
    private readonly ProviderRegistration                _registration;
    private readonly ProviderResiliencePipeline          _resilience;
    private readonly ProviderSessionManager              _sessionManager;
    private readonly IConnectionMultiplexer              _redis;
    private readonly RabbitMQ.Client.IConnection         _rabbit;
    private readonly Func<OperationResponseMessage, Task> _publishResult;
    private readonly ILogger                             _logger;

    private ProviderRequestConsumer? _consumer;
    private HeartbeatMonitor?        _heartbeat;
    private readonly CancellationTokenSource _sessionCts = new();

    public ProviderSession(
        string                               sessionId,
        IAsyncStreamReader<FromProvider>     fromProvider,
        IServerStreamWriter<ToProvider>      toProvider,
        ServerCallContext                     callContext,
        ProviderRegistration                 registration,
        ProviderResiliencePipeline           resilience,
        ProviderSessionManager               sessionManager,
        IConnectionMultiplexer               redis,
        RabbitMQ.Client.IConnection          rabbit,
        Func<OperationResponseMessage, Task> publishResult,
        ILogger                              logger)
    {
        SessionId       = sessionId;
        ProviderId      = registration.ProviderId;
        _fromProvider   = fromProvider;
        _toProvider     = toProvider;
        _callContext    = callContext;
        _registration   = registration;
        _resilience     = resilience;
        _sessionManager = sessionManager;
        _redis          = redis;
        _rabbit         = rabbit;
        _publishResult  = publishResult;
        _logger         = logger;
    }

    public async Task RunAsync(System.Security.Claims.ClaimsPrincipal jwtClaims, CancellationToken hostCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token, hostCt);
        var ct = linked.Token;

        try
        {
            // ── Phase 1: Hello handshake ──────────────────────────────────────────
            using var helloTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var helloLinked  = CancellationTokenSource.CreateLinkedTokenSource(helloTimeout.Token, ct);

            Hello? hello = null;
            try
            {
                if (!await _fromProvider.MoveNext(helloLinked.Token))
                    return;
                hello = _fromProvider.Current.Hello;
            }
            catch (OperationCanceledException) when (helloTimeout.IsCancellationRequested)
            {
                throw new RpcException(new GrpcStatus(StatusCode.DeadlineExceeded, "Hello not received within 5 seconds"));
            }

            if (hello is null)
                throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument, "First message must be Hello"));

            // ── Phase 2: Validate Hello ───────────────────────────────────────────
            var jwtSub = jwtClaims.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
            if (!string.Equals(jwtSub, hello.ProviderId, StringComparison.Ordinal))
                throw new RpcException(new GrpcStatus(StatusCode.Unauthenticated, "jwt.sub does not match Hello.providerId"));

            var registeredOps = new HashSet<string>(_registration.Operations, StringComparer.OrdinalIgnoreCase);
            var unsupported   = hello.SupportedOperations.Where(op => !registeredOps.Contains(op)).ToList();
            if (unsupported.Count > 0)
                throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument,
                    $"Unregistered operations: {string.Join(", ", unsupported)}"));

            var purpose = jwtClaims.FindFirst("purpose")?.Value;
            if (string.Equals(purpose, "probe", StringComparison.Ordinal)
                && hello.SupportedOperations.Count > 0)
            {
                throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument,
                    "Probe sessions cannot declare supported operations"));
            }
            if (!string.Equals(purpose, "probe", StringComparison.Ordinal)
                && hello.SupportedOperations.Count == 0)
            {
                throw new RpcException(new GrpcStatus(StatusCode.InvalidArgument,
                    "Provider must declare at least one supported operation"));
            }

            // ── Phase 3: Send Welcome ─────────────────────────────────────────────
            const int heartbeatIntervalSeconds = 30;
            await _toProvider.WriteAsync(new ToProvider
            {
                Welcome = new Welcome
                {
                    SessionId                = SessionId,
                    MaxConcurrentRequests    = _registration.MaxConcurrentRequests,
                    HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
                }
            }, ct);

            _logger.LogInformation("Provider {ProviderId} connected. SessionId={SessionId}", ProviderId, SessionId);
            _sessionManager.Register(SessionId, ProviderId, CloseAsync);

            // ── Phase 4: Start heartbeat + RefreshAuth timers ─────────────────────
            // Timeout = 3× interval: provider sends its first beat one full interval
            // after Welcome, so the threshold must comfortably exceed the interval.
            _heartbeat = new HeartbeatMonitor(() => CloseAsync("idle_timeout"),
                timeoutSeconds: heartbeatIntervalSeconds * 3);

            var jwtExp    = jwtClaims.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            var expUnixMs = jwtExp is not null && long.TryParse(jwtExp, out var expSec)
                ? expSec * 1000L
                : DateTimeOffset.UtcNow.AddSeconds(900).ToUnixTimeMilliseconds();

            ScheduleRefreshAuth(expUnixMs, ct);
            ScheduleForceClose(expUnixMs, ct);

            if (!string.Equals(purpose, "probe", StringComparison.Ordinal))
            {
                _consumer = await ProviderRequestConsumer.StartAsync(
                    _rabbit, ProviderId, _registration.MaxConcurrentRequests,
                    _toProvider, _publishResult, _logger, ct);
            }

            // ── Phase 5: Message loop ─────────────────────────────────────────────
            while (await _fromProvider.MoveNext(ct))
            {
                var msg = _fromProvider.Current;
                switch (msg.MessageCase)
                {
                    case FromProvider.MessageOneofCase.ResponseChunk:
                        await HandleResponseChunkAsync(msg.ResponseChunk, ct);
                        break;

                    case FromProvider.MessageOneofCase.Heartbeat:
                        _heartbeat.RecordHeartbeat();
                        break;

                    case FromProvider.MessageOneofCase.None:
                    default:
                        break;
                }
            }
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task HandleResponseChunkAsync(OperationResponseChunk chunk, CancellationToken ct)
    {
        if (chunk.ChunkCase == OperationResponseChunk.ChunkOneofCase.Progress)
        {
            var sub     = _redis.GetSubscriber();
            var channel = RedisChannel.Literal($"rp:sse-notify:{chunk.RequestId}");
            var payload = $"{{\"percent\":{chunk.Progress.Percent},\"message\":\"{chunk.Progress.Message}\"}}";
            await sub.PublishAsync(channel, payload);
        }
        else if (chunk.ChunkCase == OperationResponseChunk.ChunkOneofCase.Terminal)
        {
            _consumer?.DeliverChunk(chunk);
        }
    }

    private void ScheduleRefreshAuth(long expUnixMs, CancellationToken ct)
    {
        var refreshAtMs = expUnixMs - 60_000;
        var delayMs     = refreshAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var delay       = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero;

        _ = Task.Delay(delay, ct).ContinueWith(async _ =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await _toProvider.WriteAsync(new ToProvider
                {
                    RefreshAuth = new RefreshAuthRequired
                    {
                        CurrentTokenExpiresAtUnixMs = expUnixMs,
                        Reason                      = "token_expiring_soon",
                    }
                }, ct);
            }
            catch { /* stream may already be closing */ }
        }, ct, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void ScheduleForceClose(long expUnixMs, CancellationToken ct)
    {
        var delayMs = expUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var delay   = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero;
        _ = Task.Delay(delay, ct).ContinueWith(_ => CloseAsync("token_expired"),
            ct, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public async Task CloseAsync(string reason)
    {
        if (_sessionCts.IsCancellationRequested) return;
        _sessionCts.Cancel();
        try
        {
            await _toProvider.WriteAsync(new ToProvider
            {
                Disconnect = new Disconnect { Reason = reason }
            });
        }
        catch { /* best-effort */ }
    }

    private async Task CleanupAsync()
    {
        _sessionManager.Unregister(SessionId);
        if (_heartbeat is not null) await _heartbeat.DisposeAsync();
        if (_consumer  is not null) await _consumer.DisposeAsync();
        _sessionCts.Dispose();
        _logger.LogInformation("Session {SessionId} for provider {ProviderId} ended", SessionId, ProviderId);
    }
}
