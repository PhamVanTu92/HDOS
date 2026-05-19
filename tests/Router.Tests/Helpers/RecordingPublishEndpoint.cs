using MassTransit;

namespace ReportingPlatform.Router.Tests.Helpers;

/// <summary>Captures every Publish call so tests can assert on the published messages.</summary>
public sealed class RecordingPublishEndpoint : IPublishEndpoint
{
    public List<object> Published { get; } = new();

    public Task Publish<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        Published.Add(message!);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe,
        CancellationToken cancellationToken = default)
        where T : class
    {
        Published.Add(message!);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe,
        CancellationToken cancellationToken = default)
        where T : class
    {
        Published.Add(message!);
        return Task.CompletedTask;
    }

    public Task Publish(object message, CancellationToken cancellationToken = default)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType,
        CancellationToken cancellationToken = default)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, IPipe<PublishContext> publishPipe,
        CancellationToken cancellationToken = default)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe,
        CancellationToken cancellationToken = default)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    // MassTransit 8 additional generic overloads (object message with T type hint)
    Task IPublishEndpoint.Publish<T>(object message, CancellationToken cancellationToken)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    Task IPublishEndpoint.Publish<T>(object message, IPipe<PublishContext<T>> publishPipe,
        CancellationToken cancellationToken)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    Task IPublishEndpoint.Publish<T>(object message, IPipe<PublishContext> publishPipe,
        CancellationToken cancellationToken)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) =>
        throw new NotSupportedException();
}
