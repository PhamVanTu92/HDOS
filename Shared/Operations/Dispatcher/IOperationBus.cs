namespace ReportingPlatform.Operations.Dispatcher;

/// <summary>
/// Thin publish-only abstraction over the MassTransit bus.
/// Allows <see cref="RequestSubmissionService"/> to be tested without a full IBus mock.
/// </summary>
public interface IOperationBus
{
    Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
        where T : class;
}
