using MassTransit;
using ReportingPlatform.EventProcessor.Services;

namespace ReportingPlatform.EventProcessor.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="IngestEventEnvelope"/> messages from the
/// <c>events.raw</c> topic exchange. Thin plumbing — delegates to
/// <see cref="EventProcessorService"/> for testable domain logic.
/// </summary>
internal sealed class IngestEventConsumer : IConsumer<IngestEventEnvelope>
{
    private readonly EventProcessorService _processor;

    public IngestEventConsumer(EventProcessorService processor) => _processor = processor;

    public Task Consume(ConsumeContext<IngestEventEnvelope> context)
        => _processor.ProcessAsync(context.Message, context.CancellationToken);
}
