using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.Operations.Tests.Helpers;
using ReportingPlatform.Providers.Abstractions;
using ReportingPlatform.Providers.Models;

namespace ReportingPlatform.Operations.Tests.Dispatcher;

/// <summary>
/// Phase 5 ships an RBAC stub: RequiredRole is stored on OperationRegistration
/// but not enforced — enforcement is wired in Phase 6 via IUserRoleChecker.
/// These tests confirm the Phase 5 stub behaviour:
///   • A request where the operation requires a role the user does NOT have
///     still passes through and is submitted (stub always allows).
///   • A request where the user has the required role also passes through.
/// </summary>
public sealed class RbacStubTests
{
    // ------------------------------------------------------------------
    // Fakes (re-use same pattern as RequestSubmissionServiceTests)
    // ------------------------------------------------------------------

    private sealed class FakeRegistry : IOperationRegistry
    {
        private readonly OperationRegistration? _reg;
        public FakeRegistry(OperationRegistration? reg) => _reg = reg;

        public Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default) =>
            Task.FromResult(_reg);

        public Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OperationRegistration>>([]);

        public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingBus : IOperationBus
    {
        public int PublishCount;
        public Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
            where T : class
        {
            Interlocked.Increment(ref PublishCount);
            return Task.CompletedTask;
        }
    }

    private sealed class SingleClaimIdempotency : IIdempotencyService
    {
        public Task<bool> TryClaimAsync(string tenantId, string requestId,
            TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult(true); // always claim (unique per test)
    }

    // ------------------------------------------------------------------
    // Builder
    // ------------------------------------------------------------------

    private static OperationRegistration ActiveOp(string? requiredRole = null) => new()
    {
        OperationPattern = "secure.op",
        HandlerType      = "internal",
        Status           = "active",
        TimeoutMs        = 30_000,
        RequiredRole     = requiredRole,
    };

    private static RequestEnvelope MakeEnvelope(string userId = "u1") => new()
    {
        RequestId = $"req-{Guid.NewGuid():N}",
        Operation = "secure.op",
        Params    = JsonDocument.Parse("{}").RootElement,
        TenantId  = "t1",
        UserId    = userId,
    };

    private static RequestSubmissionService MakeService(
        OperationRegistration reg,
        RecordingBus bus) =>
        new(
            new FakeRegistry(reg),
            FakeParamsValidator.AlwaysValid(),
            new SingleClaimIdempotency(),
            bus,
            NullLogger<RequestSubmissionService>.Instance);

    // ------------------------------------------------------------------
    // Rbac_Phase5Stub_RequiredRoleNotEnforced_RequestPasses
    // ------------------------------------------------------------------

    [Fact]
    public async Task Rbac_Phase5Stub_RequiredRoleNotEnforced_RequestPasses()
    {
        // Operation requires "admin" role; user has no role claim (Phase 6 not wired)
        var bus = new RecordingBus();
        var svc = MakeService(ActiveOp(requiredRole: "admin"), bus);

        // Phase 5 stub: should NOT throw — RBAC not enforced yet
        var ack = await svc.SubmitAsync(MakeEnvelope(userId: "ordinary-user"), null);

        Assert.NotNull(ack.RequestId);
        Assert.Equal(1, bus.PublishCount); // message was published
    }

    // ------------------------------------------------------------------
    // Rbac_Phase5Stub_NoRequiredRole_RequestPasses
    // ------------------------------------------------------------------

    [Fact]
    public async Task Rbac_Phase5Stub_NoRequiredRole_RequestPasses()
    {
        // Operation has no RequiredRole — should always pass
        var bus = new RecordingBus();
        var svc = MakeService(ActiveOp(requiredRole: null), bus);

        var ack = await svc.SubmitAsync(MakeEnvelope(), null);

        Assert.NotNull(ack.RequestId);
        Assert.Equal(1, bus.PublishCount);
    }
}
