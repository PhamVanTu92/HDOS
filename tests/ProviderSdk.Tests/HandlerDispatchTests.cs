extern alias SdkAlias;

namespace ReportingPlatform.ProviderSdk.Tests;

/// <summary>SD9–SD12: Handler dispatch, cancellation, and traceparent propagation.</summary>
public sealed class HandlerDispatchTests
{
    private sealed record EchoParams  { public string Message { get; init; } = ""; }
    private sealed record EchoResult  { public string Echo    { get; init; } = ""; }

    private sealed class EchoHandler : SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>
    {
        public Task<SdkAlias::ReportingPlatform.ProviderSdk.OperationResult<EchoResult>> HandleAsync(
            SdkAlias::ReportingPlatform.ProviderSdk.OperationContext<EchoParams> ctx, CancellationToken ct)
            => Task.FromResult(SdkAlias::ReportingPlatform.ProviderSdk.OperationResult<EchoResult>.Success(
                new EchoResult { Echo = ctx.Params.Message }));
    }

    private sealed class ThrowingHandler : SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>
    {
        public Task<SdkAlias::ReportingPlatform.ProviderSdk.OperationResult<EchoResult>> HandleAsync(
            SdkAlias::ReportingPlatform.ProviderSdk.OperationContext<EchoParams> ctx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException("simulated cancel");
        }
    }

    private sealed class CancelAwareHandler : SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;

        public async Task<SdkAlias::ReportingPlatform.ProviderSdk.OperationResult<EchoResult>> HandleAsync(
            SdkAlias::ReportingPlatform.ProviderSdk.OperationContext<EchoParams> ctx, CancellationToken ct)
        {
            _started.TrySetResult();
            while (!ct.IsCancellationRequested)
                await Task.Delay(10, CancellationToken.None);
            return SdkAlias::ReportingPlatform.ProviderSdk.OperationResult<EchoResult>.Cancelled();
        }
    }

    private sealed class TraceCapturingHandler : SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>
    {
        public string? CapturedTraceId { get; private set; }

        public Task<SdkAlias::ReportingPlatform.ProviderSdk.OperationResult<EchoResult>> HandleAsync(
            SdkAlias::ReportingPlatform.ProviderSdk.OperationContext<EchoParams> ctx, CancellationToken ct)
        {
            CapturedTraceId = Activity.Current?.TraceId.ToString();
            return Task.FromResult(SdkAlias::ReportingPlatform.ProviderSdk.OperationResult<EchoResult>.Success(
                new EchoResult { Echo = "traced" }));
        }
    }

    private static Welcome MakeWelcome(string sessionId = "test-session") => new Welcome
    {
        SessionId               = sessionId,
        MaxConcurrentRequests   = 4,
        HeartbeatIntervalSeconds = 30,
    };

    private static OperationRequest MakeRequest(string requestId = "req-1", string? traceparent = null) =>
        new OperationRequest
        {
            RequestId  = requestId,
            Operation  = "echo.test",
            ParamsJson = """{"message":"hello"}""",
            TenantId   = "t1",
            UserId     = "u1",
            TimeoutAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(),
            Traceparent = traceparent ?? string.Empty,
        };

    // SD9 — Request dispatched → Terminal(DONE) with payload
    [Fact]
    public async Task SD9_RequestDispatched_TerminalDoneWithPayload()
    {
        await using var bridge = await FakeBridgeServer.StartAsync();

        var handler = new FakeTokenHandler();
        handler.SetupSuccess();
        var opts = TestHelpers.DefaultOpts(bridgeEndpoint: bridge.Address);

        var services = new ServiceCollection();
        services.AddSingleton<EchoHandler>();
        services.AddSingleton<SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>>(
            sp => sp.GetRequiredService<EchoHandler>());
        var sp = services.BuildServiceProvider();

        var registry = new HandlerRegistry();
        registry.Register<EchoParams, EchoResult>("echo.test");

        var mgr = TestHelpers.BuildConnectionManager(
            TestHelpers.BuildTokenManager(handler, opts),
            registry, opts, new SdkCallbacks(), new RecordingDelay(), sp);

        using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var task = mgr.StartAsync(cts.Token);

        var s1 = await bridge.WaitForSessionAsync(cts.Token);
        await s1.WaitForMessageAsync(m => m.MessageCase == FromProvider.MessageOneofCase.Hello, TimeSpan.FromSeconds(3));
        await s1.SendAsync(new ToProvider { Welcome = MakeWelcome() });
        await s1.SendAsync(new ToProvider { Request = MakeRequest("req-9") });

        var terminal = await s1.WaitForMessageAsync(
            m => m.MessageCase == FromProvider.MessageOneofCase.ResponseChunk
              && m.ResponseChunk.ChunkCase == OperationResponseChunk.ChunkOneofCase.Terminal,
            TimeSpan.FromSeconds(5));

        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { });

        Assert.Equal("req-9", terminal.ResponseChunk.RequestId);
        Assert.Equal(ProtoStatus.Done, terminal.ResponseChunk.Terminal.Status);
        Assert.Contains("echo", terminal.ResponseChunk.Terminal.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    // SD10 — Handler throws OperationCanceledException → Terminal(CANCELLED)
    [Fact]
    public async Task SD10_HandlerThrowsOperationCanceledException_TerminalCancelled()
    {
        await using var bridge = await FakeBridgeServer.StartAsync();

        var handler = new FakeTokenHandler();
        handler.SetupSuccess();
        var opts = TestHelpers.DefaultOpts(bridgeEndpoint: bridge.Address);

        var services = new ServiceCollection();
        services.AddSingleton<ThrowingHandler>();
        services.AddSingleton<SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>>(
            sp => sp.GetRequiredService<ThrowingHandler>());
        var sp = services.BuildServiceProvider();

        var registry = new HandlerRegistry();
        registry.Register<EchoParams, EchoResult>("echo.test");

        var mgr = TestHelpers.BuildConnectionManager(
            TestHelpers.BuildTokenManager(handler, opts),
            registry, opts, new SdkCallbacks(), new RecordingDelay(), sp);

        using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var task = mgr.StartAsync(cts.Token);

        var s1 = await bridge.WaitForSessionAsync(cts.Token);
        await s1.WaitForMessageAsync(m => m.MessageCase == FromProvider.MessageOneofCase.Hello, TimeSpan.FromSeconds(3));
        await s1.SendAsync(new ToProvider { Welcome = MakeWelcome() });
        await s1.SendAsync(new ToProvider { Request = MakeRequest("req-10") });

        var terminal = await s1.WaitForMessageAsync(
            m => m.MessageCase == FromProvider.MessageOneofCase.ResponseChunk
              && m.ResponseChunk.ChunkCase == OperationResponseChunk.ChunkOneofCase.Terminal,
            TimeSpan.FromSeconds(5));

        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { });

        Assert.Equal("req-10", terminal.ResponseChunk.RequestId);
        Assert.Equal(ProtoStatus.Cancelled, terminal.ResponseChunk.Terminal.Status);
    }

    // SD11 — Cancel message → handler CT cancelled → Terminal(CANCELLED)
    [Fact]
    public async Task SD11_CancelMessage_HandlerCtCancelled_TerminalCancelled()
    {
        await using var bridge = await FakeBridgeServer.StartAsync();

        var handler = new FakeTokenHandler();
        handler.SetupSuccess();
        var opts = TestHelpers.DefaultOpts(bridgeEndpoint: bridge.Address);

        var services    = new ServiceCollection();
        var cancelHandler = new CancelAwareHandler();
        services.AddSingleton<SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>>(cancelHandler);
        var sp = services.BuildServiceProvider();

        var registry = new HandlerRegistry();
        registry.Register<EchoParams, EchoResult>("echo.test");

        var mgr = TestHelpers.BuildConnectionManager(
            TestHelpers.BuildTokenManager(handler, opts),
            registry, opts, new SdkCallbacks(), new RecordingDelay(), sp);

        using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var task = mgr.StartAsync(cts.Token);

        var s1 = await bridge.WaitForSessionAsync(cts.Token);
        await s1.WaitForMessageAsync(m => m.MessageCase == FromProvider.MessageOneofCase.Hello, TimeSpan.FromSeconds(3));
        await s1.SendAsync(new ToProvider { Welcome = MakeWelcome() });
        await s1.SendAsync(new ToProvider { Request = MakeRequest("req-11") });

        // Wait for handler to start
        await cancelHandler.Started.WaitAsync(TimeSpan.FromSeconds(3));

        // Send Cancel
        await s1.SendAsync(new ToProvider { Cancel = new Cancel { RequestId = "req-11" } });

        var terminal = await s1.WaitForMessageAsync(
            m => m.MessageCase == FromProvider.MessageOneofCase.ResponseChunk
              && m.ResponseChunk.ChunkCase == OperationResponseChunk.ChunkOneofCase.Terminal,
            TimeSpan.FromSeconds(5));

        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { });

        Assert.Equal("req-11", terminal.ResponseChunk.RequestId);
        Assert.Equal(ProtoStatus.Cancelled, terminal.ResponseChunk.Terminal.Status);
    }

    // SD12 — traceparent propagated → Activity.Current.TraceId inside handler matches
    [Fact]
    public async Task SD12_Traceparent_Propagated_ActivityTraceIdMatches()
    {
        await using var bridge = await FakeBridgeServer.StartAsync();

        var handler = new FakeTokenHandler();
        handler.SetupSuccess();
        var opts = TestHelpers.DefaultOpts(bridgeEndpoint: bridge.Address);

        var traceHandler = new TraceCapturingHandler();
        var services     = new ServiceCollection();
        services.AddSingleton<SdkAlias::ReportingPlatform.ProviderSdk.IOperationHandler<EchoParams, EchoResult>>(traceHandler);
        var sp = services.BuildServiceProvider();

        var registry = new HandlerRegistry();
        registry.Register<EchoParams, EchoResult>("echo.test");

        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "ReportingPlatform.ProviderSdk",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted  = _ => { },
            ActivityStopped  = _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        var mgr = TestHelpers.BuildConnectionManager(
            TestHelpers.BuildTokenManager(handler, opts),
            registry, opts, new SdkCallbacks(), new RecordingDelay(), sp);

        using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var task = mgr.StartAsync(cts.Token);

        var s1 = await bridge.WaitForSessionAsync(cts.Token);
        await s1.WaitForMessageAsync(m => m.MessageCase == FromProvider.MessageOneofCase.Hello, TimeSpan.FromSeconds(3));
        await s1.SendAsync(new ToProvider { Welcome = MakeWelcome() });
        await s1.SendAsync(new ToProvider
        {
            Request = new OperationRequest
            {
                RequestId   = "req-12",
                Operation   = "echo.test",
                ParamsJson  = """{"message":"traced"}""",
                TenantId    = "t1",
                UserId      = "u1",
                TimeoutAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds(),
                Traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            }
        });

        var terminal = await s1.WaitForMessageAsync(
            m => m.MessageCase == FromProvider.MessageOneofCase.ResponseChunk
              && m.ResponseChunk.ChunkCase == OperationResponseChunk.ChunkOneofCase.Terminal,
            TimeSpan.FromSeconds(5));

        cts.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { });

        Assert.Equal(ProtoStatus.Done, terminal.ResponseChunk.Terminal.Status);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", traceHandler.CapturedTraceId);
    }
}
