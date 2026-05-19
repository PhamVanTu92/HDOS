namespace ReportingPlatform.Operations.Dispatcher;

/// <summary>
/// Thin abstraction for publishing a cancel request message.
/// Used by both Request.Api (HTTP cancel endpoint) and Realtime.Hub (CancelRequest method)
/// so that both transports produce identical cancel signals without duplicating MassTransit logic.
/// </summary>
public interface ICancelBus
{
    Task PublishCancelAsync(string requestId, string userId, string tenantId,
        CancellationToken ct = default);
}
