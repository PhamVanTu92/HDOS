using MassTransit;

namespace ReportingPlatform.Operations.Dispatcher;

/// <summary>
/// Production adapter that wraps MassTransit <see cref="IBus"/> as <see cref="IOperationBus"/>.
/// </summary>
public sealed class MassTransitOperationBus : IOperationBus
{
    private readonly IBus _bus;

    public MassTransitOperationBus(IBus bus) => _bus = bus;

    public Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
        where T : class =>
        _bus.Publish(message, ctx => ctx.SetRoutingKey(routingKey), ct);
}
