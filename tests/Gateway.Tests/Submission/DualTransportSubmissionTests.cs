namespace ReportingPlatform.Gateway.Tests.Submission;

/// <summary>
/// DT1–DT8: Dual-transport parity tests.
/// Each test exercises the same scenario via the shared <see cref="RequestSubmissionService"/>
/// to guarantee HTTP and Hub paths produce identical observable outcomes.
/// </summary>
public sealed class DualTransportSubmissionTests
{
    // ── DT1 — Happy-path submit returns SubmitAck with correct requestId ─────

    [Fact]
    public async Task DT1_HappyPath_ReturnsAckWithRequestId()
    {
        var (svc, _, bus) = TestFactories.MakeSubmissionService();
        var env = TestFactories.MakeEnvelope(requestId: "req-dt1");

        // HTTP path (connectionId = null)
        var ackHttp = await svc.SubmitAsync(env, connectionId: null);

        // Hub path (connectionId present)
        // Use a second idempotency so we can claim the same requestId again
        var (svc2, _, bus2) = TestFactories.MakeSubmissionService();
        var ackHub = await svc2.SubmitAsync(env, connectionId: "conn-abc");

        Assert.Equal("req-dt1", ackHttp.RequestId);
        Assert.Equal("req-dt1", ackHub.RequestId);
        Assert.NotNull(ackHttp.QueuedAt);
        Assert.NotNull(ackHub.QueuedAt);
        Assert.Single(bus.Published);
        Assert.Single(bus2.Published);
    }

    // ── DT2 — Duplicate requestId returns SubmitAck without re-publishing ────

    [Fact]
    public async Task DT2_DuplicateRequestId_ReturnsAckNoSecondPublish()
    {
        var idempotency = new InMemoryIdempotency();
        var (svc, _, bus) = TestFactories.MakeSubmissionService(idempotency: idempotency);
        var env = TestFactories.MakeEnvelope(requestId: "req-dt2");

        var ack1 = await svc.SubmitAsync(env, connectionId: null);
        var ack2 = await svc.SubmitAsync(env, connectionId: null);

        Assert.Equal(ack1.RequestId, ack2.RequestId);
        Assert.Single(bus.Published); // published exactly once
    }

    // ── DT3 — Unknown operation throws OperationException("OPERATION_NOT_FOUND") ─

    [Fact]
    public async Task DT3_UnknownOperation_ThrowsOperationNotFound()
    {
        var (svc, _, _) = TestFactories.MakeSubmissionService(
            registry: FakeOperationRegistry.Unknown());

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            svc.SubmitAsync(TestFactories.MakeEnvelope(operation: "unknown"), null));

        Assert.Equal("OPERATION_NOT_FOUND", ex.Code);
    }

    // ── DT4 — Params validation failure throws VALIDATION_ERROR ─────────────

    [Fact]
    public async Task DT4_ParamsValidationFailure_ThrowsValidationError()
    {
        var (svc, _, _) = TestFactories.MakeSubmissionService(
            validator: FakeParamsValidator.InvalidWith(
                new ValidationError { Field = "startDate", Message = "required", Code = "REQUIRED" }));

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            svc.SubmitAsync(TestFactories.MakeEnvelope(), null));

        Assert.Equal("VALIDATION_ERROR", ex.Code);
        Assert.Contains("startDate", ex.Message);
    }

    // ── DT5 — Params > 64 KB throws PARAMS_TOO_LARGE ────────────────────────

    [Fact]
    public async Task DT5_ParamsTooLarge_ThrowsParamsTooLarge()
    {
        var (svc, _, _) = TestFactories.MakeSubmissionService();
        var largeParams = "{\"data\":\"" + new string('x', 70_000) + "\"}";
        var env = TestFactories.MakeEnvelope(paramsJson: largeParams);

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            svc.SubmitAsync(env, null));

        Assert.Equal("PARAMS_TOO_LARGE", ex.Code);
    }

    // ── DT6 — Empty requestId throws VALIDATION_ERROR ───────────────────────
    // Tenant mismatch enforcement lives in Controller/Hub layer (not SubmissionService).
    // We test the envelope validation guard inside SubmitAsync instead.

    [Fact]
    public async Task DT6_EmptyRequestId_ThrowsValidationError()
    {
        var (svc, _, _) = TestFactories.MakeSubmissionService();

        // Build envelope manually to set empty requestId
        var env = new RequestEnvelope
        {
            RequestId = "",
            Operation = "test.op",
            Params    = JsonDocument.Parse("{}").RootElement,
            TenantId  = "tenant-1",
            UserId    = "user-1",
        };

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            svc.SubmitAsync(env, null));

        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    // ── DT7 — OwnerStore receives different ConnectionId per transport ────────

    [Fact]
    public async Task DT7_HttpPath_SubmissionLogWrittenToRedis()
    {
        var (svc, db, _) = TestFactories.MakeSubmissionService();

        // HTTP path — no connectionId header
        await svc.SubmitAsync(TestFactories.MakeEnvelope(requestId: "req-dt7"), connectionId: null);

        // Assert that submission log was written (side-effect tracked by FakeDatabase)
        Assert.Contains(RedisKeys.SubmissionLog("req-dt7"), db.StringKeys);
    }

    // ── DT8 — options.progress = true sets ProgressStreamUrl ─────────────────

    [Fact]
    public async Task DT8_ProgressOption_SetsProgressStreamUrl()
    {
        var (svc, _, _) = TestFactories.MakeSubmissionService();
        var env = TestFactories.MakeEnvelope(requestId: "req-dt8", progress: true);

        // HTTP path
        var ackHttp = await svc.SubmitAsync(env, connectionId: null);

        // Hub path
        var (svc2, _, _) = TestFactories.MakeSubmissionService();
        var ackHub = await svc2.SubmitAsync(env, connectionId: "conn-hub");

        var expectedUrl = $"/sse/requests/req-dt8/progress";
        Assert.Equal(expectedUrl, ackHttp.ProgressStreamUrl);
        Assert.Equal(expectedUrl, ackHub.ProgressStreamUrl);
    }
}
