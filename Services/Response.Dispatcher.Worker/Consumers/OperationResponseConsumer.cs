namespace ReportingPlatform.ResponseDispatcher.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="OperationResponseMessage"/>.
/// Thin shell — delegates all routing logic to <see cref="ResponseRouter"/>.
/// </summary>
public sealed class OperationResponseConsumer(
    ResponseRouter router,
    ILogger<OperationResponseConsumer> logger) : IConsumer<OperationResponseMessage>
{
    public async Task Consume(ConsumeContext<OperationResponseMessage> ctx)
    {
        var msg = ctx.Message;
        logger.LogDebug("Consuming response requestId={RequestId} status={Status}",
            msg.RequestId, msg.Status);
        await router.RouteAsync(msg, ctx.CancellationToken);
    }
}
