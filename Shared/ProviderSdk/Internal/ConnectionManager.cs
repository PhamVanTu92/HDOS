namespace ReportingPlatform.ProviderSdk.Internal;

internal sealed class ConnectionManager : BackgroundService
{
    private readonly TokenManager     _tokenManager;
    private readonly HandlerRegistry  _handlerRegistry;
    private readonly ProviderSdkOptions _opts;
    private readonly IServiceProvider _sp;
    private readonly SdkCallbacks     _callbacks;
    private readonly IDelay           _delay;
    private readonly ILogger<ConnectionManager> _logger;

    private static readonly int[] BackoffStepsMs = [1000, 2000, 4000, 8000, 16000, 30000];
    private volatile bool _stopped;

    public ConnectionManager(
        TokenManager tokenManager,
        HandlerRegistry handlerRegistry,
        ProviderSdkOptions opts,
        IServiceProvider sp,
        SdkCallbacks callbacks,
        IDelay delay,
        ILogger<ConnectionManager> logger)
    {
        _tokenManager    = tokenManager;
        _handlerRegistry = handlerRegistry;
        _opts            = opts;
        _sp              = sp;
        _callbacks       = callbacks;
        _delay           = delay;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int attempt = 0;

        while (!stoppingToken.IsCancellationRequested && !_stopped)
        {
            // ── AcquiringJwt ─────────────────────────────────────────────
            string token;
            try
            {
                token = await _tokenManager.AcquireAsync(stoppingToken);
            }
            catch (ProviderSdkConfigurationException ex)
            {
                _logger.LogCritical(ex, "SDK configuration error — stopping permanently");
                break;
            }
            catch (CredentialsRevokedException)
            {
                _logger.LogCritical("Credentials revoked — SDK will not reconnect");
                _stopped = true;
                if (_callbacks.OnCredentialsRevoked is not null)
                    await _callbacks.OnCredentialsRevoked();
                break;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                var backoff = GetBackoff(attempt++);
                _logger.LogWarning(ex, "Token acquisition failed; retry in {Delay}", backoff);
                try { await _delay.DelayAsync(backoff, stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            // ── OpeningChannel + Active + Refreshing ──────────────────────
            try
            {
                bool resetBackoff = await ConnectAndServeAsync(token, stoppingToken);
                if (resetBackoff) attempt = 0;
            }
            catch (ProviderSdkConfigurationException ex)
            {
                _logger.LogCritical(ex, "Configuration error from Bridge — stopping");
                break;
            }
            catch (CredentialsRevokedException)
            {
                _stopped = true;
                if (_callbacks.OnCredentialsRevoked is not null)
                    await _callbacks.OnCredentialsRevoked();
                break;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!_stopped)
            {
                var backoff = GetBackoff(attempt++);
                _logger.LogWarning(ex, "Connection error; reconnecting in {Delay}", backoff);
                if (_callbacks.OnReconnecting is not null)
                    await _callbacks.OnReconnecting(attempt, backoff);
                try { await _delay.DelayAsync(backoff, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("ConnectionManager stopped");
    }

    /// <summary>Returns true when handshake succeeded (caller should reset backoff counter).</summary>
    private async Task<bool> ConnectAndServeAsync(string token, CancellationToken ct)
    {
        var channelOptions = new GrpcChannelOptions();
        if (_opts.GrpcHttpHandler is not null)
            channelOptions.HttpHandler = _opts.GrpcHttpHandler;

        using var channel = GrpcChannel.ForAddress(_opts.BridgeEndpoint, channelOptions);
        var client = new OperationProvider.OperationProviderClient(channel);

        var headers = new Metadata { { "authorization", $"Bearer {token}" } };
        using var stream = client.Connect(headers, cancellationToken: ct);

        // Send Hello
        var hello = new Hello
        {
            ProviderId = _opts.ProviderId,
            Version    = _opts.Version,
        };
        hello.SupportedOperations.AddRange(_handlerRegistry.RegisteredOperations);
        await stream.RequestStream.WriteAsync(new FromProvider { Hello = hello }, ct);

        // Await Welcome (5s deadline)
        using var welcomeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        welcomeCts.CancelAfter(TimeSpan.FromSeconds(5));

        Welcome? welcome = null;
        try
        {
            await foreach (var msg in stream.ResponseStream.ReadAllAsync(welcomeCts.Token))
            {
                switch (msg.MessageCase)
                {
                    case ToProvider.MessageOneofCase.Welcome:
                        welcome = msg.Welcome;
                        goto gotWelcome;
                    case ToProvider.MessageOneofCase.Disconnect:
                        HandleDisconnectReason(msg.Disconnect.Reason);
                        return false;
                }
            }
        }
        catch (OperationCanceledException) when (welcomeCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for Welcome from Bridge (5s).");
        }
        gotWelcome:

        if (welcome is null)
            throw new InvalidOperationException("Bridge stream ended before sending Welcome.");

        _logger.LogInformation("Connected — sessionId={SessionId}", welcome.SessionId);
        if (_callbacks.OnConnected is not null)
            await _callbacks.OnConnected(welcome.SessionId, welcome);

        // ── Active phase ─────────────────────────────────────────────────
        await ServeAsync(stream, welcome, ct);
        await channel.ShutdownAsync();
        return true; // handshake succeeded — caller resets backoff
    }

    private async Task ServeAsync(
        AsyncDuplexStreamingCall<FromProvider, ToProvider> stream,
        Welcome welcome,
        CancellationToken ct)
    {
        // Single write lock shared by heartbeat + progress reporters + terminal writers
        var writeLock   = new SemaphoreSlim(1, 1);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat   = new HeartbeatSender(stream.RequestStream, writeLock, welcome.HeartbeatIntervalSeconds, heartbeatCts.Token);
        heartbeat.Start();

        var dispatcher = new RequestDispatcher(
            _handlerRegistry, _sp, stream.RequestStream, writeLock,
            _opts.MaxConcurrentRequests, _logger);

        bool refreshRequested = false;
        try
        {
            await foreach (var msg in stream.ResponseStream.ReadAllAsync(ct))
            {
                switch (msg.MessageCase)
                {
                    case ToProvider.MessageOneofCase.Request:
                        dispatcher.DispatchFireAndForget(msg.Request, ct);
                        break;
                    case ToProvider.MessageOneofCase.Cancel:
                        dispatcher.Cancel(msg.Cancel.RequestId);
                        break;
                    case ToProvider.MessageOneofCase.RefreshAuth:
                        refreshRequested = true;
                        goto doneReading;
                    case ToProvider.MessageOneofCase.Disconnect:
                        HandleDisconnectReason(msg.Disconnect.Reason);
                        goto doneReading;
                }
            }
            doneReading:;
        }
        finally
        {
            heartbeatCts.Cancel();
            await heartbeat.DisposeAsync();
        }

        if (_callbacks.OnDisconnected is not null)
            await _callbacks.OnDisconnected(refreshRequested ? "refresh_auth" : "stream_ended");

        if (refreshRequested)
            await RefreshingPhaseAsync(dispatcher, stream, ct);
    }

    private async Task RefreshingPhaseAsync(
        RequestDispatcher dispatcher,
        AsyncDuplexStreamingCall<FromProvider, ToProvider> stream,
        CancellationToken ct)
    {
        dispatcher.HoldNew = true;

        // Wait up to RefreshingDrainTimeout for in-flight to drain (default 30s; reduced in tests)
        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        drainCts.CancelAfter(_opts.RefreshingDrainTimeout);
        try
        {
            await dispatcher.WaitForDrainAsync(drainCts.Token);
        }
        catch (OperationCanceledException) when (drainCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Refreshing drain timeout — cancelling in-flight handlers");
            dispatcher.CancelAll();
            // Give handlers RefreshingForceCloseDelay to observe cancellation and write Terminal(CANCELLED)
            try { await _delay.DelayAsync(_opts.RefreshingForceCloseDelay, ct); } catch (OperationCanceledException) { }
        }

        try { await stream.RequestStream.CompleteAsync(); } catch { }
        // Caller (ConnectAndServeAsync) will proceed to AcquireAsync for fresh token
    }

    private void HandleDisconnectReason(string reason)
    {
        _logger.LogWarning("Disconnect received: {Reason}", reason);
        if (string.Equals(reason, "credentials_revoked", StringComparison.OrdinalIgnoreCase))
            throw new CredentialsRevokedException();
        // "server_shutdown", "idle_timeout", "provider_suspended" — reconnect with backoff (caller handles)
    }

    private TimeSpan GetBackoff(int attempt)
    {
        var baseMs  = BackoffStepsMs[Math.Min(attempt, BackoffStepsMs.Length - 1)];
        var jitter  = baseMs * _opts.ReconnectJitterFraction * (Random.Shared.NextDouble() * 2 - 1); // ±jitter%
        var totalMs = Math.Clamp(baseMs + jitter, 100, _opts.MaxReconnectDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(totalMs);
    }
}
