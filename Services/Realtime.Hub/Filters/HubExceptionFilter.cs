using ReportingPlatform.Contracts.Exceptions;

namespace ReportingPlatform.RealtimeHub.Filters;

/// <summary>
/// Global Hub filter that converts <see cref="OperationException"/> thrown by hub methods
/// into <see cref="HubException"/> with the error code as the message and a detail payload.
/// This keeps hub method bodies clean — they throw OperationException; the filter translates.
/// </summary>
public sealed class HubExceptionFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (OperationException ex)
        {
            // Translate to HubException so the client sees the typed error code.
            // The `data` field carries additional context where applicable.
            throw new HubException(ex.Code, ex);
        }
        // Other exceptions propagate as-is — SignalR logs and sends a generic error.
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        => next(context);

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
        => next(context, exception);
}
