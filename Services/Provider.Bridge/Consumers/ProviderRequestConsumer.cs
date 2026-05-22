using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Responses;
using ReportingPlatform.Contracts.Serialization;

namespace ReportingPlatform.Bridge.Consumers;

public sealed class ProviderRequestConsumer : IAsyncDisposable
{
    private readonly IChannel    _channel;
    private readonly string      _queueName;
    private string               _consumerTag;
    private readonly ILogger     _logger;
    private bool                 _disposed;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<Terminal>> _pending = new();
    private readonly IServerStreamWriter<ToProvider> _toProvider;
    private readonly Func<OperationResponseMessage, Task> _publishResult;
    private readonly ConcurrentDictionary<ulong, bool> _deliveryTags = new();

    private ProviderRequestConsumer(
        IChannel channel,
        string queueName,
        IServerStreamWriter<ToProvider> toProvider,
        Func<OperationResponseMessage, Task> publishResult,
        ILogger logger)
    {
        _channel       = channel;
        _queueName     = queueName;
        _consumerTag   = string.Empty;
        _toProvider    = toProvider;
        _publishResult = publishResult;
        _logger        = logger;
    }

    public static async Task<ProviderRequestConsumer> StartAsync(
        IConnection                          rabbitConnection,
        string                               providerId,
        int                                  maxConcurrent,
        IServerStreamWriter<ToProvider>      toProvider,
        Func<OperationResponseMessage, Task> publishResult,
        ILogger                              logger,
        CancellationToken                    ct = default)
    {
        var channel   = await rabbitConnection.CreateChannelAsync(cancellationToken: ct);
        var queueName = $"q.provider.{providerId}";

        // Ensure the parent exchange exists before binding. Request.Api/Worker
        // declare it lazily on first publish/consume, but a fresh bridge session
        // may start before either has done so — bind would then fail with NOT_FOUND.
        // Fanout matches MassTransit's default for SetEntityName.
        await channel.ExchangeDeclareAsync(
            exchange:   "operation.request",
            type:       "fanout",
            durable:    true,
            autoDelete: false,
            cancellationToken: ct);

        try
        {
            await channel.QueueDeclareAsync(
                queue:      queueName,
                durable:    true,
                exclusive:  false,
                autoDelete: false,
                arguments:  new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = "operation.request.dlq",
                    ["x-message-ttl"]          = 600_000,
                },
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex.Message.Contains("PRECONDITION_FAILED"))
        {
            logger.LogWarning("Queue {Queue} already exists with different arguments; using existing queue. " +
                              "Manual delete required to change TTL/DLX.", queueName);
        }

        await channel.QueueBindAsync(
            queue:      queueName,
            exchange:   "operation.request",
            routingKey: $"provider.{providerId}",
            cancellationToken: ct);

        await channel.BasicQosAsync(0, (ushort)maxConcurrent, false, ct);

        var consumer = new ProviderRequestConsumer(channel, queueName, toProvider, publishResult, logger);

        var asyncConsumer = new AsyncEventingBasicConsumer(channel);
        asyncConsumer.ReceivedAsync += consumer.OnMessageReceivedAsync;

        var tag = await channel.BasicConsumeAsync(
            queue:    queueName,
            autoAck:  false,
            consumer: asyncConsumer,
            cancellationToken: ct);

        consumer._consumerTag = tag;
        return consumer;
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var deliveryTag = ea.DeliveryTag;
        _deliveryTags.TryAdd(deliveryTag, true);

        OperationRequestMessage? msg = null;
        try
        {
            msg = DeserializeRequest(ea.Body.Span);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize OperationRequestMessage");
            await _channel.BasicNackAsync(deliveryTag, false, false);
            _deliveryTags.TryRemove(deliveryTag, out _);
            return;
        }

        if (msg is null)
        {
            await _channel.BasicNackAsync(deliveryTag, false, false);
            _deliveryTags.TryRemove(deliveryTag, out _);
            return;
        }

        var tcs = new TaskCompletionSource<Terminal>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[msg.RequestId] = tcs;

        try
        {
            _logger.LogInformation("Forwarding request {RequestId} (op={Operation}) to provider stream",
                msg.RequestId, msg.Operation);
            await _toProvider.WriteAsync(new ToProvider
            {
                Request = new OperationRequest
                {
                    RequestId       = msg.RequestId,
                    Operation       = msg.Operation,
                    ParamsJson      = msg.ParamsJson,
                    TenantId        = msg.TenantId,
                    UserId          = msg.UserId,
                    TimeoutAtUnixMs = msg.TimeoutAtUnixMs,
                    WantsProgress   = msg.WantsProgress,
                    Traceparent     = msg.Traceparent ?? string.Empty,
                    CorrelationId   = msg.CorrelationId ?? string.Empty,
                }
            });

            var remainingMs = msg.TimeoutAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using var cts   = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(remainingMs, 1_000)));
            var terminal    = await tcs.Task.WaitAsync(cts.Token);

            var response = BuildResponse(msg, terminal);
            await _publishResult(response);
            await _channel.BasicAckAsync(deliveryTag, false);
            _deliveryTags.TryRemove(deliveryTag, out _);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request {RequestId}", msg.RequestId);
            var errorResponse = new OperationResponseMessage
            {
                RequestId     = msg.RequestId,
                Operation     = msg.Operation,
                Status        = ResponseStatus.Failed,
                TenantId      = msg.TenantId,
                UserId        = msg.UserId,
                ConnectionId  = msg.ConnectionId,
                CorrelationId = msg.CorrelationId,
                ElapsedMs     = 0,
                Error         = new ErrorDetail { Code = "PROVIDER_ERROR", Message = ex.Message },
            };
            await _publishResult(errorResponse);
            await _channel.BasicAckAsync(deliveryTag, false);
            _deliveryTags.TryRemove(deliveryTag, out _);
        }
        finally
        {
            _pending.TryRemove(msg.RequestId, out _);
        }
    }

    public void DeliverChunk(OperationResponseChunk chunk)
    {
        if (chunk.ChunkCase == OperationResponseChunk.ChunkOneofCase.Terminal)
        {
            if (_pending.TryGetValue(chunk.RequestId, out var tcs))
                tcs.TrySetResult(chunk.Terminal);
        }
    }

    // Accepts both a raw OperationRequestMessage JSON and a MassTransit envelope
    // ({ "messageType": [...], "message": { ...actual fields... } }). request-api
    // publishes via MassTransit (enveloped); direct/manual publishes are raw.
    private static OperationRequestMessage? DeserializeRequest(ReadOnlySpan<byte> body)
    {
        using var doc = JsonDocument.Parse(body.ToArray());
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("message", out var inner) &&
            inner.ValueKind == JsonValueKind.Object)
        {
            // MassTransit envelope — deserialize the nested "message" object.
            return inner.Deserialize(MessagingContractsJsonContext.Default.OperationRequestMessage);
        }

        // Raw message.
        return JsonSerializer.Deserialize(body, MessagingContractsJsonContext.Default.OperationRequestMessage);
    }

    private static OperationResponseMessage BuildResponse(OperationRequestMessage msg, Terminal terminal)
    {
        return new OperationResponseMessage
        {
            RequestId     = msg.RequestId,
            Operation     = msg.Operation,
            Status        = terminal.Status switch
            {
                ReportingPlatform.Provider.V1.Status.Done      => ResponseStatus.Done,
                ReportingPlatform.Provider.V1.Status.Failed    => ResponseStatus.Failed,
                ReportingPlatform.Provider.V1.Status.Cancelled => ResponseStatus.Cancelled,
                _                                              => ResponseStatus.Failed,
            },
            PayloadJson   = terminal.Status == ReportingPlatform.Provider.V1.Status.Done ? terminal.PayloadJson : null,
            TenantId      = msg.TenantId,
            UserId        = msg.UserId,
            ConnectionId  = msg.ConnectionId,
            CorrelationId = msg.CorrelationId,
            ElapsedMs     = terminal.ElapsedMs,
            Error         = terminal.Error is { Code.Length: > 0 }
                ? new ErrorDetail { Code = terminal.Error.Code, Message = terminal.Error.Message }
                : null,
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (tag, _) in _deliveryTags)
        {
            try { await _channel.BasicNackAsync(tag, false, true); }
            catch { /* best-effort */ }
        }

        foreach (var (_, tcs) in _pending)
            tcs.TrySetCanceled();

        if (!string.IsNullOrEmpty(_consumerTag))
        {
            try { await _channel.BasicCancelAsync(_consumerTag); }
            catch { /* best-effort */ }
        }

        try { await _channel.CloseAsync(); }
        catch { /* best-effort */ }
        _channel.Dispose();
    }
}
