using ReportingPlatform.Contracts.Responses;
using ReportingPlatform.Operations.Progress;

namespace ReportingPlatform.Operations.Dispatcher;

public sealed class OperationDispatcher
{
    private readonly OperationHandlerRegistry _registry;
    private readonly IParamsValidator _validator;
    private readonly IProgressBuffer _progressBuffer;
    private readonly ILogger<OperationDispatcher> _logger;

    public OperationDispatcher(
        OperationHandlerRegistry registry,
        IParamsValidator validator,
        IProgressBuffer progressBuffer,
        ILogger<OperationDispatcher> logger)
    {
        _registry       = registry;
        _validator      = validator;
        _progressBuffer = progressBuffer;
        _logger         = logger;
    }

    public async Task<OperationResponseMessage> DispatchAsync(
        OperationRequestMessage message,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Step 1: Deadline check
        if (message.TimeoutAtUnixMs < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            return Fail(message, "DEADLINE_EXCEEDED", "Request deadline exceeded before dispatch.",
                ResponseStatus.Timeout, sw);
        }

        // Step 2: Resolve handler
        var handler = _registry.Resolve(message.Operation);
        if (handler is null)
        {
            return Fail(message, "HANDLER_NOT_FOUND",
                $"No handler registered for operation '{message.Operation}'.",
                ResponseStatus.Failed, sw);
        }

        // Step 3: Validate params
        JsonElement paramsEl;
        try
        {
            paramsEl = JsonDocument.Parse(message.ParamsJson).RootElement;
        }
        catch (JsonException ex)
        {
            return Fail(message, "INVALID_PARAMS", $"Params JSON parse error: {ex.Message}",
                ResponseStatus.Failed, sw);
        }

        var validation = await _validator.ValidateAsync(message.Operation, paramsEl, ct);
        if (!validation.IsValid)
        {
            var detailsJson = JsonSerializer.Serialize(validation.Errors,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new OperationResponseMessage
            {
                RequestId     = message.RequestId,
                Status        = ResponseStatus.Failed,
                TenantId      = message.TenantId,
                UserId        = message.UserId,
                ConnectionId  = message.ConnectionId,
                CorrelationId = message.CorrelationId,
                ElapsedMs     = sw.ElapsedMilliseconds,
                Error = new ErrorDetail
                {
                    Code        = "VALIDATION_ERROR",
                    Message     = "One or more parameter validation errors occurred.",
                    DetailsJson = detailsJson,
                },
            };
        }

        // Step 4: Build context + deadline token
        var remainingMs = message.TimeoutAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(remainingMs, 1)));

        // Step 5: Dispatch handler (with optional progress reporter)
        try
        {
            using var activity = ActivitySources.Operations.StartActivity("operation.dispatch");
            activity?.SetTag("operation.name", message.Operation);
            activity?.SetTag("tenant.id", message.TenantId);
            activity?.SetTag("request.id", message.RequestId);

            JsonElement result;

            if (message.WantsProgress)
            {
                await using var reporter = new ProgressReporter(_progressBuffer, message.RequestId);
                var context = BuildContext(message, paramsEl, reporter);
                result = await handler.HandleAsync(context, cts.Token);
            }
            else
            {
                var context = BuildContext(message, paramsEl, null);
                result = await handler.HandleAsync(context, cts.Token);
            }

            return new OperationResponseMessage
            {
                RequestId     = message.RequestId,
                Status        = ResponseStatus.Done,
                PayloadJson   = result.GetRawText(),
                TenantId      = message.TenantId,
                UserId        = message.UserId,
                ConnectionId  = message.ConnectionId,
                CorrelationId = message.CorrelationId,
                ElapsedMs     = sw.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException)
        {
            return Fail(message, "OPERATION_TIMEOUT", "Operation timed out.",
                ResponseStatus.Timeout, sw);
        }
        catch (OperationException ex)
        {
            return Fail(message, ex.Code, ex.Message, ResponseStatus.Failed, sw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception in handler for operation={Operation} requestId={RequestId}",
                message.Operation, message.RequestId);
            return Fail(message, "INTERNAL_ERROR", "An internal error occurred.",
                ResponseStatus.Failed, sw);
        }
    }

    private static OperationHandlerContext BuildContext(
        OperationRequestMessage message,
        JsonElement paramsEl,
        IProgress<ProgressUpdate>? progress) =>
        new()
        {
            RequestId   = message.RequestId,
            TenantId    = message.TenantId,
            UserId      = message.UserId,
            Params      = paramsEl,
            Progress    = progress,
            Traceparent = message.Traceparent,
        };

    private static OperationResponseMessage Fail(
        OperationRequestMessage message,
        string code,
        string errorMessage,
        ResponseStatus status,
        Stopwatch sw) =>
        new()
        {
            RequestId     = message.RequestId,
            Status        = status,
            TenantId      = message.TenantId,
            UserId        = message.UserId,
            ConnectionId  = message.ConnectionId,
            CorrelationId = message.CorrelationId,
            ElapsedMs     = sw.ElapsedMilliseconds,
            Error = new ErrorDetail
            {
                Code    = code,
                Message = errorMessage,
            },
        };
}
