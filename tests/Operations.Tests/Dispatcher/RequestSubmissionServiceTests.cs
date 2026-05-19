using System.Collections.Concurrent;
using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.Operations.Tests.Helpers;
using ReportingPlatform.Providers.Abstractions;
using ReportingPlatform.Providers.Models;

namespace ReportingPlatform.Operations.Tests.Dispatcher;

public sealed class RequestSubmissionServiceTests
{
    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class FakeOperationRegistry : IOperationRegistry
    {
        private readonly OperationRegistration? _registration;

        public FakeOperationRegistry(OperationRegistration? registration = null) =>
            _registration = registration;

        public Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default) =>
            Task.FromResult(_registration);

        public Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OperationRegistration>>([]);

        public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryIdempotency : IIdempotencyService
    {
        private readonly ConcurrentDictionary<string, bool> _claimed = new();

        public Task<bool> TryClaimAsync(string tenantId, string requestId,
            TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult(_claimed.TryAdd($"{tenantId}:{requestId}", true));
    }

    private sealed class RecordingBus : IOperationBus
    {
        public List<(object Message, string RoutingKey)> Published = new();

        public Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
            where T : class
        {
            Published.Add((message!, routingKey));
            return Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    // Builders
    // ------------------------------------------------------------------

    private static OperationRegistration ActiveRegistration(string op = "test.op") =>
        new()
        {
            OperationPattern = op,
            HandlerType      = "internal",
            Status           = "active",
            TimeoutMs        = 30_000,
        };

    private static RequestEnvelope MakeEnvelope(
        string operation = "test.op",
        string requestId = "req-1",
        string paramsJson = "{}") =>
        new()
        {
            RequestId   = requestId,
            Operation   = operation,
            Params      = JsonDocument.Parse(paramsJson).RootElement,
            TenantId    = "t1",
            UserId      = "u1",
        };

    private static RequestSubmissionService MakeService(
        IOperationRegistry? registry = null,
        RecordingBus? bus = null,
        InMemoryIdempotency? idempotency = null) =>
        new(
            registry ?? new FakeOperationRegistry(ActiveRegistration()),
            FakeParamsValidator.AlwaysValid(),
            idempotency ?? new InMemoryIdempotency(),
            bus ?? new RecordingBus(),
            NullLogger<RequestSubmissionService>.Instance);

    // ------------------------------------------------------------------
    // Submit_UnknownOperation_Throws
    // ------------------------------------------------------------------

    [Fact]
    public async Task Submit_UnknownOperation_Throws()
    {
        var svc = MakeService(registry: new FakeOperationRegistry(registration: null));

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            svc.SubmitAsync(MakeEnvelope("unknown"), null));

        Assert.Equal("OPERATION_NOT_FOUND", ex.Code);
    }

    // ------------------------------------------------------------------
    // Submit_InactiveOperation_Throws
    // ------------------------------------------------------------------

    [Fact]
    public async Task Submit_InactiveOperation_Throws()
    {
        var reg = ActiveRegistration() with { Status = "inactive" };
        var svc = MakeService(registry: new FakeOperationRegistry(reg));

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            svc.SubmitAsync(MakeEnvelope(), null));

        Assert.Equal("OPERATION_NOT_ACTIVE", ex.Code);
    }

    // ------------------------------------------------------------------
    // Submit_ParamsTooLarge_Throws
    // ------------------------------------------------------------------

    [Fact]
    public async Task Submit_ParamsTooLarge_Throws()
    {
        var svc         = MakeService();
        var largeParams = "{\"data\":\"" + new string('x', 70_000) + "\"}";

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            svc.SubmitAsync(MakeEnvelope(paramsJson: largeParams), null));

        Assert.Equal("PARAMS_TOO_LARGE", ex.Code);
    }

    // ------------------------------------------------------------------
    // Submit_Valid_PublishesToQueue
    // ------------------------------------------------------------------

    [Fact]
    public async Task Submit_Valid_PublishesToPriorityQueue()
    {
        var bus = new RecordingBus();
        var svc = MakeService(bus: bus);

        var ack = await svc.SubmitAsync(MakeEnvelope(), connectionId: null);

        Assert.Equal("req-1", ack.RequestId);
        Assert.NotNull(ack.QueuedAt);
        Assert.Single(bus.Published);
        var (published, routingKey) = bus.Published[0];
        var msg = (OperationRequestMessage)published;
        Assert.Equal("test.op", msg.Operation);
        Assert.Equal("t1",      msg.TenantId);
        Assert.Equal("operation.request.normal", routingKey);
    }

    // ------------------------------------------------------------------
    // Submit_SameRequestId_SecondCall_ReturnsImmediately
    // ------------------------------------------------------------------

    [Fact]
    public async Task Submit_SameRequestId_SecondCall_ReturnsImmediately()
    {
        var bus         = new RecordingBus();
        var idempotency = new InMemoryIdempotency();
        var svc         = MakeService(bus: bus, idempotency: idempotency);

        var envelope = MakeEnvelope(requestId: "req-idem");
        var ack1 = await svc.SubmitAsync(envelope, null);
        var ack2 = await svc.SubmitAsync(envelope, null);

        Assert.Equal(ack1.RequestId, ack2.RequestId);
        Assert.Single(bus.Published); // only published once — second call was idempotent
    }
}
