using MassTransit;
using ReportingPlatform.Contracts.Messaging;
using ReportingPlatform.Operations.Dispatcher;

namespace ReportingPlatform.Router.Consumers;

/// <summary>
/// Thin MassTransit consumer — receive OperationRequestMessage → dispatch → publish response.
/// All operation logic lives in OperationDispatcher; this class is pure plumbing.
/// Registered once; bound to three queues (high / normal / low) in Program.cs.
/// </summary>
public sealed class OperationRequestConsumer : IConsumer<OperationRequestMessage>
{
    private readonly OperationDispatcher _dispatcher;
    private readonly IPublishEndpoint    _publish;
    private readonly ILogger<OperationRequestConsumer> _logger;

    public OperationRequestConsumer(
        OperationDispatcher dispatcher,
        IPublishEndpoint publish,
        ILogger<OperationRequestConsumer> logger)
    {
        _dispatcher = dispatcher;
        _publish    = publish;
        _logger     = logger;
    }

    // MassTransit entry point — delegates to the testable HandleAsync.
    public Task Consume(ConsumeContext<OperationRequestMessage> ctx) =>
        HandleAsync(ctx.Message, ctx.CancellationToken);

    /// <summary>
    /// Core dispatch logic — internal so Router.Tests can call it directly
    /// without needing to implement the full ConsumeContext interface.
    /// </summary>
    internal async Task HandleAsync(OperationRequestMessage msg, CancellationToken ct = default)
    {
        // Restore W3C trace context so this span is a child of the submit span.
        if (!string.IsNullOrEmpty(msg.Traceparent))
            Activity.Current?.SetParentId(msg.Traceparent);

        _logger.LogInformation(
            "Routing operation={Operation} requestId={RequestId} tenantId={TenantId} priority={Priority}",
            msg.Operation, msg.RequestId, msg.TenantId, msg.Priority);

        using var activity = ActivitySources.Operations.StartActivity("operation.consume");
        activity?.SetTag("operation.name", msg.Operation);
        activity?.SetTag("tenant.id",      msg.TenantId);
        activity?.SetTag("request.id",     msg.RequestId);

        var response = await _dispatcher.DispatchAsync(msg, ct);

        if (response.Status is ResponseStatus.Timeout or ResponseStatus.Failed)
        {
            _logger.LogWarning(
                "Operation {Operation} requestId={RequestId} status={Status} code={Code}",
                msg.Operation, msg.RequestId, response.Status, response.Error?.Code);
        }
        else
        {
            _logger.LogInformation(
                "Operation {Operation} requestId={RequestId} status={Status} elapsedMs={ElapsedMs}",
                msg.Operation, msg.RequestId, response.Status, response.ElapsedMs);
        }

        await _publish.Publish(response, ct);
    }
}
