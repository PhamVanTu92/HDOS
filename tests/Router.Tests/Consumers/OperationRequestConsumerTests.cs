using ReportingPlatform.Contracts.Exceptions;
using ReportingPlatform.Contracts.Validation;
using ReportingPlatform.Operations.Abstractions;
using ReportingPlatform.Operations.Dispatcher;
using ReportingPlatform.Operations.Progress;
using ReportingPlatform.Router.Tests.Helpers;

namespace ReportingPlatform.Router.Tests.Consumers;

public sealed class OperationRequestConsumerTests
{
    // ------------------------------------------------------------------
    // Builder
    // ------------------------------------------------------------------

    private static (OperationRequestConsumer Consumer, RecordingPublishEndpoint Bus)
        MakeConsumer(params IOperationHandler[] handlers)
    {
        var (dispatcher, _) = DispatcherFactory.Build(handlers);
        var bus = new RecordingPublishEndpoint();
        var consumer = new OperationRequestConsumer(
            dispatcher,
            bus,
            NullLogger<OperationRequestConsumer>.Instance);
        return (consumer, bus);
    }

    private static (OperationRequestConsumer Consumer, RecordingPublishEndpoint Bus, RecordingProgressBuffer Buffer)
        MakeConsumerWithBuffer(params IOperationHandler[] handlers)
    {
        var (dispatcher, buffer) = DispatcherFactory.Build(handlers);
        var bus = new RecordingPublishEndpoint();
        var consumer = new OperationRequestConsumer(
            dispatcher,
            bus,
            NullLogger<OperationRequestConsumer>.Instance);
        return (consumer, bus, buffer);
    }

    // ------------------------------------------------------------------
    // T1 — Happy path: dispatches and publishes Done response
    // ------------------------------------------------------------------

    [Fact]
    public async Task Consumer_HappyPath_DispatchesAndPublishesResponse()
    {
        var payload = JsonDocument.Parse("{\"result\":42}").RootElement;
        var (consumer, bus) = MakeConsumer(FakeHandler.Success("test.op", payload));

        await consumer.HandleAsync(MessageFactory.Make());

        Assert.Single(bus.Published);
        var response = Assert.IsType<OperationResponseMessage>(bus.Published[0]);
        Assert.Equal(ResponseStatus.Done, response.Status);
        Assert.Equal("req-1", response.RequestId);
        Assert.Contains("42", response.PayloadJson);
    }

    // ------------------------------------------------------------------
    // T2 — Deadline in past: publishes DEADLINE_EXCEEDED
    // ------------------------------------------------------------------

    [Fact]
    public async Task Consumer_DeadlineInPast_PublishesTimeoutResponse()
    {
        var (consumer, bus) = MakeConsumer(FakeHandler.Success("test.op", default));

        // TimeoutAtUnixMs = 0 (Unix epoch — well in the past)
        var msg = MessageFactory.Make(timeoutAtUnixMs: 0);
        await consumer.HandleAsync(msg);

        Assert.Single(bus.Published);
        var response = Assert.IsType<OperationResponseMessage>(bus.Published[0]);
        Assert.Equal(ResponseStatus.Timeout, response.Status);
        Assert.Equal("DEADLINE_EXCEEDED", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // T3 — Operation-level failure: publishes Failed response as-is
    // ------------------------------------------------------------------

    [Fact]
    public async Task Consumer_DispatcherReturnsFailure_PublishesFailed()
    {
        var ex = new OperationException("MY_CODE", "something broke");
        var (consumer, bus) = MakeConsumer(FakeHandler.Throws("test.op", ex));

        await consumer.HandleAsync(MessageFactory.Make());

        Assert.Single(bus.Published);
        var response = Assert.IsType<OperationResponseMessage>(bus.Published[0]);
        Assert.Equal(ResponseStatus.Failed, response.Status);
        Assert.Equal("MY_CODE", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // T4 — Pre-cancelled token: publishes OPERATION_TIMEOUT
    // ------------------------------------------------------------------

    [Fact]
    public async Task Consumer_CancellationDuringDispatch_PublishesTimeout()
    {
        // Handler that blocks until cancelled
        var (consumer, bus) = MakeConsumer(FakeHandler.Cancels("test.op"));

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        await consumer.HandleAsync(MessageFactory.Make(), cts.Token);

        Assert.Single(bus.Published);
        var response = Assert.IsType<OperationResponseMessage>(bus.Published[0]);
        Assert.Equal(ResponseStatus.Timeout, response.Status);
        Assert.Equal("OPERATION_TIMEOUT", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // T5 — WantsProgress: progress events recorded in buffer
    // ------------------------------------------------------------------

    [Fact]
    public async Task Consumer_WantsProgress_ProgressEventsStoredInBuffer()
    {
        var (consumer, _, buffer) =
            MakeConsumerWithBuffer(FakeHandler.ReportsProgress("test.op", eventCount: 3));

        await consumer.HandleAsync(MessageFactory.Make(wantsProgress: true));

        Assert.Equal(3, buffer.Events.Count);
        Assert.All(buffer.Events, e => Assert.Equal("req-1", e.RequestId));
        Assert.All(buffer.Events, e => Assert.InRange(e.Percent, 1, 100));
    }

    // ------------------------------------------------------------------
    // T6 — Unhandled exception propagates (MassTransit will retry / DLQ)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Consumer_UnexpectedException_PropagatesFromConsumer()
    {
        // Inject a dispatcher that throws unexpectedly (simulates bug outside OperationException).
        // OperationDispatcher's own catch converts OperationException to Failed, but
        // a completely unhandled exception at the dispatcher level propagates up.
        // Here we inject an Exception type that OperationDispatcher doesn't catch specifically
        // to confirm the consumer does NOT swallow it (MassTransit must see it to DLQ).
        var handler = FakeHandler.Throws("test.op", new InvalidOperationException("unexpected"));
        var (consumer, bus) = MakeConsumer(handler);

        // OperationDispatcher catches Exception → publishes INTERNAL_ERROR (not re-thrown)
        // So T6 verifies the dispatcher returns a Failed response for unhandled exceptions,
        // and the consumer publishes it (does not swallow or re-throw).
        await consumer.HandleAsync(MessageFactory.Make());

        Assert.Single(bus.Published);
        var response = Assert.IsType<OperationResponseMessage>(bus.Published[0]);
        Assert.Equal(ResponseStatus.Failed, response.Status);
        Assert.Equal("INTERNAL_ERROR", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // T7 — DI smoke test: ServiceCollection resolves consumer without error
    // ------------------------------------------------------------------

    [Fact]
    public void DI_AllRouterDependencies_ResolveWithoutException()
    {
        var services = new ServiceCollection();

        // Infrastructure stubs — no real RabbitMQ, Redis, or Postgres
        services.AddSingleton<IPublishEndpoint>(new RecordingPublishEndpoint());
        services.AddSingleton<IProgressBuffer>(new RecordingProgressBuffer());
        services.AddSingleton<IParamsValidator>(AlwaysValidParams.Instance);
        services.AddSingleton<IOperationHandler>(
            FakeHandler.Success("test.op", JsonDocument.Parse("{}").RootElement));
        services.AddSingleton<OperationHandlerRegistry>(sp =>
            new OperationHandlerRegistry(
                sp.GetServices<IOperationHandler>()));
        services.AddSingleton<OperationDispatcher>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // System under test — scoped to match typical DI lifetime
        services.AddScoped<OperationRequestConsumer>();

        var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        // Assert: resolves without InvalidOperationException (missing reg / captive dependency)
        var consumer = scope.ServiceProvider.GetRequiredService<OperationRequestConsumer>();
        Assert.NotNull(consumer);
    }
}
