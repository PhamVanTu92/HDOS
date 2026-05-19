using ReportingPlatform.Caching;
using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.Contracts.Operations;
using ReportingPlatform.Contracts.Store;
using ReportingPlatform.Providers.Abstractions;
using StackExchange.Redis;

namespace ReportingPlatform.Operations.Dispatcher;

public sealed class RequestSubmissionService : INestedRequestSubmitter
{
    private const int MaxParamsSizeBytes = 65_536;
    private const int MaxTimeoutMs       = 300_000; // 5 minutes hard cap

    // Submission log TTL = 3 × MessageTtlMs (10 min). Provides 30-min orphan-detection window.
    private static readonly TimeSpan SubmissionLogTtl = TimeSpan.FromMinutes(30);

    private readonly IOperationRegistry  _operationRegistry;
    private readonly IParamsValidator    _paramsValidator;
    private readonly IIdempotencyService _idempotency;
    private readonly IOperationBus       _bus;
    private readonly OwnerStore?         _ownerStore;   // null in unit-test mode
    private readonly IDatabase?          _redis;        // null in unit-test mode
    private readonly ILogger<RequestSubmissionService> _logger;

    // ── Production constructor (full deps) ────────────────────────────────────
    public RequestSubmissionService(
        IOperationRegistry operationRegistry,
        IParamsValidator paramsValidator,
        IIdempotencyService idempotency,
        IOperationBus bus,
        OwnerStore? ownerStore,
        IDatabase? redis,
        ILogger<RequestSubmissionService> logger)
    {
        _operationRegistry = operationRegistry;
        _paramsValidator   = paramsValidator;
        _idempotency       = idempotency;
        _bus               = bus;
        _ownerStore        = ownerStore;
        _redis             = redis;
        _logger            = logger;
    }

    // ── Unit-test constructor (no Redis side-effects) ─────────────────────────
    // Operations.Tests uses this overload so it doesn't need Caching deps.
    internal RequestSubmissionService(
        IOperationRegistry operationRegistry,
        IParamsValidator paramsValidator,
        IIdempotencyService idempotency,
        IOperationBus bus,
        ILogger<RequestSubmissionService> logger)
        : this(operationRegistry, paramsValidator, idempotency, bus,
               ownerStore: null, redis: null, logger) { }

    public async Task<SubmitAck> SubmitAsync(
        RequestEnvelope envelope,
        string? connectionId,
        CancellationToken ct = default)
    {
        // Step 1: Resolve operation from registry
        var registration = await _operationRegistry.ResolveAsync(envelope.Operation, ct);
        if (registration is null)
            throw new OperationException("OPERATION_NOT_FOUND",
                $"Operation '{envelope.Operation}' is not registered.");

        if (!string.Equals(registration.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new OperationException("OPERATION_NOT_ACTIVE",
                $"Operation '{envelope.Operation}' is not active.");

        // Step 2: RBAC stub — IUserRoleChecker wired in Phase 7 (§11.3)
        // RequiredRole check deferred — real JWT claim extraction injected when real auth is added.

        // Step 3: Layer 1 envelope validation
        if (string.IsNullOrWhiteSpace(envelope.RequestId))
            throw new OperationException("VALIDATION_ERROR", "requestId must be non-empty.");

        var paramsJson      = envelope.Params.GetRawText();
        var paramsSizeBytes = System.Text.Encoding.UTF8.GetByteCount(paramsJson);
        if (paramsSizeBytes > MaxParamsSizeBytes)
            throw new OperationException("PARAMS_TOO_LARGE",
                $"Params JSON size {paramsSizeBytes} bytes exceeds limit of {MaxParamsSizeBytes} bytes.");

        // Step 4: Layer 2 params validation
        var validation = await _paramsValidator.ValidateAsync(envelope.Operation, envelope.Params, ct);
        if (!validation.IsValid)
        {
            var details = string.Join("; ", validation.Errors.Select(e => $"{e.Field}: {e.Message}"));
            throw new OperationException("VALIDATION_ERROR",
                $"Parameter validation failed: {details}");
        }

        // Step 5: Compute effective timeout
        var effectiveMs = Math.Min(
            envelope.Options.TimeoutMs ?? registration.TimeoutMs,
            MaxTimeoutMs);
        var timeoutAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + effectiveMs;

        // Step 6: Idempotency claim (key = requestId)
        var idempotencyTtl = TimeSpan.FromMilliseconds(effectiveMs * 2);
        var claimed = await _idempotency.TryClaimAsync(
            envelope.TenantId, envelope.RequestId, idempotencyTtl, ct);

        if (!claimed)
        {
            _logger.LogDebug("Idempotent re-submission for requestId={RequestId}", envelope.RequestId);
            return BuildAck(envelope);
        }

        // Step 6b: Record ownership in Redis (null-safe — skipped in unit-test mode)
        if (_ownerStore is not null)
        {
            await _ownerStore.SetAsync(new OwnerStoreRecord
            {
                RequestId    = envelope.RequestId,
                ConnectionId = connectionId,
                UserId       = envelope.UserId,
                TenantId     = envelope.TenantId,
                SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
            }, ct);
        }

        // Step 6c: Submission log (orphan detection marker — TTL 30 min)
        if (_redis is not null)
        {
            await _redis.StringSetAsync(
                RedisKeys.SubmissionLog(envelope.RequestId),
                "1",
                SubmissionLogTtl);
        }

        // Step 6d: Active-progress tracking
        if (_redis is not null && envelope.Options.Progress)
        {
            await _redis.SetAddAsync(RedisKeys.ActiveProgress, envelope.RequestId);
        }

        // Step 7: Build OperationRequestMessage
        var traceparent = Activity.Current?.Id ?? string.Empty;
        var message = new OperationRequestMessage
        {
            RequestId       = envelope.RequestId,
            Operation       = envelope.Operation,
            ParamsJson      = paramsJson,
            TenantId        = envelope.TenantId,
            UserId          = envelope.UserId,
            CorrelationId   = envelope.CorrelationId,
            ConnectionId    = connectionId,
            TimeoutAtUnixMs = timeoutAtUnixMs,
            WantsProgress   = envelope.Options.Progress,
            Priority        = envelope.Options.Priority,
            CacheSeconds    = envelope.Options.CacheSeconds,
            Traceparent     = traceparent,
            ParentRequestId = null,
        };

        // Step 8: Publish to priority queue (internal) or provider queue (external).
        // External operations bypass the Router Worker entirely — routed directly to
        // q.provider.{providerId} which Provider.Bridge declares + binds on session start.
        string routingKey;
        if (registration.HandlerType.Equals("external", StringComparison.OrdinalIgnoreCase)
            && registration.ProviderId is not null)
        {
            routingKey = $"provider.{registration.ProviderId}";
        }
        else
        {
            routingKey = envelope.Options.Priority switch
            {
                Priority.High => "operation.request.high",
                Priority.Low  => "operation.request.low",
                _             => "operation.request.normal",
            };
        }

        await _bus.PublishAsync(message, routingKey, ct);

        _logger.LogInformation(
            "Queued requestId={RequestId} operation={Operation} tenantId={TenantId} priority={Priority}",
            envelope.RequestId, envelope.Operation, envelope.TenantId, envelope.Options.Priority);

        return BuildAck(envelope);
    }

    private static SubmitAck BuildAck(RequestEnvelope envelope) =>
        new()
        {
            RequestId         = envelope.RequestId,
            QueuedAt          = DateTimeOffset.UtcNow.ToString("O"),
            ProgressStreamUrl = envelope.Options.Progress
                ? $"/sse/requests/{envelope.RequestId}/progress"
                : null,
        };
}
