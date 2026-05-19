using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.Providers.Abstractions;

namespace ReportingPlatform.Operations.Dispatcher;

public sealed class RequestSubmissionService
{
    private const int MaxParamsSizeBytes = 65_536;
    private const int MaxTimeoutMs       = 300_000; // 5 minutes hard cap

    private readonly IOperationRegistry  _operationRegistry;
    private readonly IParamsValidator    _paramsValidator;
    private readonly IIdempotencyService _idempotency;
    private readonly IOperationBus       _bus;
    private readonly ILogger<RequestSubmissionService> _logger;

    public RequestSubmissionService(
        IOperationRegistry operationRegistry,
        IParamsValidator paramsValidator,
        IIdempotencyService idempotency,
        IOperationBus bus,
        ILogger<RequestSubmissionService> logger)
    {
        _operationRegistry = operationRegistry;
        _paramsValidator   = paramsValidator;
        _idempotency       = idempotency;
        _bus               = bus;
        _logger            = logger;
    }

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

        // Step 2: RBAC stub (Phase 6 wires real JWT claim extraction)
        // RequiredRole check deferred — IUserRoleChecker injected in Phase 6

        // Step 3: Layer 1 envelope validation
        if (string.IsNullOrWhiteSpace(envelope.RequestId))
            throw new OperationException("VALIDATION_ERROR", "requestId must be non-empty.");

        var paramsJson   = envelope.Params.GetRawText();
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
        var ttl = TimeSpan.FromMilliseconds(effectiveMs * 2);
        var claimed = await _idempotency.TryClaimAsync(
            envelope.TenantId, envelope.RequestId, ttl, ct);

        if (!claimed)
        {
            // Re-submission — return same requestId immediately without re-publishing
            _logger.LogDebug(
                "Idempotent re-submission for requestId={RequestId}", envelope.RequestId);
            return new SubmitAck
            {
                RequestId         = envelope.RequestId,
                QueuedAt          = DateTimeOffset.UtcNow.ToString("O"),
                ProgressStreamUrl = envelope.Options.Progress
                    ? $"/api/v1/progress/{envelope.RequestId}"
                    : null,
            };
        }

        // Step 7: Build OperationRequestMessage
        var traceparent = Activity.Current?.Id ?? string.Empty;
        var message = new OperationRequestMessage
        {
            RequestId        = envelope.RequestId,
            Operation        = envelope.Operation,
            ParamsJson       = paramsJson,
            TenantId         = envelope.TenantId,
            UserId           = envelope.UserId,
            CorrelationId    = envelope.CorrelationId,
            ConnectionId     = connectionId,
            TimeoutAtUnixMs  = timeoutAtUnixMs,
            WantsProgress    = envelope.Options.Progress,
            Priority         = envelope.Options.Priority,
            CacheSeconds     = envelope.Options.CacheSeconds,
            Traceparent      = traceparent,
            ParentRequestId  = null,
        };

        // Step 8: Publish to priority queue
        var routingKey = envelope.Options.Priority switch
        {
            Priority.High => "operation.request.high",
            Priority.Low  => "operation.request.low",
            _             => "operation.request.normal",
        };

        await _bus.PublishAsync(message, routingKey, ct);

        // Step 9: Return SubmitAck
        return new SubmitAck
        {
            RequestId         = envelope.RequestId,
            QueuedAt          = DateTimeOffset.UtcNow.ToString("O"),
            ProgressStreamUrl = envelope.Options.Progress
                ? $"/api/v1/progress/{envelope.RequestId}"
                : null,
        };
    }
}
