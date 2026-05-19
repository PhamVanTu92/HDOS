using MassTransit;

namespace ReportingPlatform.Operations.Dispatcher;

/// <summary>
/// Publishes <see cref="CancelRequestMessage"/> to the reporting.cancel-requests queue
/// via MassTransit. One instance is registered per service host that supports cancellation.
/// </summary>
public sealed class MassTransitCancelBus(IPublishEndpoint publish) : ICancelBus
{
    public Task PublishCancelAsync(
        string requestId,
        string userId,
        string tenantId,
        CancellationToken ct = default) =>
        publish.Publish(new CancelRequestMessage
        {
            RequestId = requestId,
            UserId    = userId,
            TenantId  = tenantId,
        }, ct);
}
