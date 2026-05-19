using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.RequestApi.Sse;

/// <summary>
/// Handles <c>GET /sse/requests/{requestId}/progress</c>.
/// <list type="number">
///   <item>Registers a bounded channel with <see cref="SseConnectionRegistry"/>.</item>
///   <item>Replays buffered events from the Redis Stream (ring buffer guarantee).</item>
///   <item>Streams live events pushed by <see cref="ProgressPubSubSubscriber"/> via the registry.</item>
///   <item>Sends a 30-second heartbeat ping to prevent proxy idle-disconnection.</item>
///   <item>Closes after the terminal event or on client disconnect.</item>
/// </list>
/// </summary>
public static class SseProgressEndpoint
{
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task HandleAsync(
        string requestId,
        HttpContext ctx,
        SseConnectionRegistry registry,
        ProgressRingBuffer ringBuffer,
        ILogger<SseProgressEndpointMarker> logger,
        CancellationToken ct)
    {
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";
        // Disable response buffering so events flush immediately.
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        // Bounded channel: 200 slots. DropOldest keeps the stream alive under backpressure.
        var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        registry.Register(requestId, channel.Writer);
        try
        {
            // ── Replay buffered events from Redis Stream ──────────────────────
            IReadOnlyList<ProgressEvent> buffered = [];
            try
            {
                buffered = await ringBuffer.ReadAsync(requestId, "0-0", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read buffered progress for requestId={RequestId}", requestId);
            }

            foreach (var evt in buffered)
            {
                var progressJson = SerializeProgress(evt);
                await WriteEventAsync(ctx.Response, "progress", progressJson, ct);
            }

            // ── Live events (pub/sub via registry) + heartbeat ────────────────
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = RunHeartbeatAsync(ctx.Response, heartbeatCts.Token);

            try
            {
                await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                {
                    await WriteEventAsync(ctx.Response, evt.Name, evt.DataJson, ct);
                    if (evt.Name == "terminal")
                        break;
                }
            }
            finally
            {
                await heartbeatCts.CancelAsync();
                try { await heartbeatTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
        finally
        {
            registry.Unregister(requestId, channel.Writer);
            channel.Writer.TryComplete();
            logger.LogDebug("SSE closed requestId={RequestId}", requestId);
        }
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        string eventName,
        string data,
        CancellationToken ct)
    {
        await response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static async Task RunHeartbeatAsync(HttpResponse response, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            // SSE comment — keeps the connection alive through proxies.
            await response.WriteAsync(": ping\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }

    private static string SerializeProgress(ProgressEvent evt)
    {
        var tsMs = DateTimeOffset.TryParse(evt.Timestamp, out var dto)
            ? dto.ToUnixTimeMilliseconds()
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return JsonSerializer.Serialize(new
        {
            requestId = evt.RequestId,
            percent   = evt.Percent,
            message   = evt.Message,
            tsUnixMs  = tsMs,
        }, _json);
    }

    // Marker type so ILogger<> generic works with a non-static class reference.
    public sealed class SseProgressEndpointMarker;
}
