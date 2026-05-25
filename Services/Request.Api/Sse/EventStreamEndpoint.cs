using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace ReportingPlatform.RequestApi.Sse;

/// <summary>
/// Handles <c>GET /sse/events</c> — a persistent SSE stream that delivers JSON events
/// to the authenticated browser client, replacing the SignalR / MessagePack WebSocket:
/// <list type="bullet">
///   <item><b>RequestCompleted</b> — terminal success result for any request submitted by this user.</item>
///   <item><b>RequestFailed</b> / <b>RequestCancelled</b> — terminal failure / cancellation.</item>
///   <item><b>WidgetStale</b> — data-change notification for widget channels the client subscribed to.</item>
/// </list>
/// Widget channels are registered at connect time via repeatable <c>?widgetChannel=…</c> query params.
/// When the token refreshes the client opens a new EventSource (same pattern as the token-refresh
/// reconnect in the old SignalR setup).
/// </summary>
public static class EventStreamEndpoint
{
    public static async Task HandleAsync(
        HttpContext ctx,
        UserSseRegistry registry,
        ILogger<EventStreamEndpointMarker> logger,
        CancellationToken ct)
    {
        // ── Identify the authenticated user ───────────────────────────────────────────
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? ctx.User.FindFirstValue("sub")
                  ?? string.Empty;

        if (string.IsNullOrEmpty(userId))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // ── Widget channels to subscribe (repeatable ?widgetChannel= query param) ─────
        var widgetChannels = ctx.Request.Query["widgetChannel"]
            .OfType<string>()                           // removes nulls, narrows to string
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToArray();

        // ── SSE response headers ──────────────────────────────────────────────────────
        ctx.Response.ContentType                   = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl          = "no-cache";
        ctx.Response.Headers.Connection            = "keep-alive";
        ctx.Response.Headers["X-Accel-Buffering"]  = "no";

        // Bounded channel: 500 slots, drop-oldest under backpressure.
        var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        // ── Register in the local registry ───────────────────────────────────────────
        registry.Register(userId, channel.Writer);
        foreach (var wc in widgetChannels)
            registry.Register(wc, channel.Writer);

        logger.LogDebug(
            "SSE /events opened userId={UserId} widgetChannels=[{Channels}]",
            userId, string.Join(", ", widgetChannels));

        try
        {
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeatTask = RunHeartbeatAsync(ctx.Response, heartbeatCts.Token);

            try
            {
                // Stream events until client disconnects (ct cancelled) or channel completes.
                await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                {
                    await WriteEventAsync(ctx.Response, evt.Name, evt.DataJson, ct);
                }
            }
            finally
            {
                await heartbeatCts.CancelAsync();
                try { await heartbeatTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
        finally
        {
            registry.Unregister(userId, channel.Writer);
            foreach (var wc in widgetChannels)
                registry.Unregister(wc, channel.Writer);

            channel.Writer.TryComplete();
            logger.LogDebug("SSE /events closed userId={UserId}", userId);
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
            await response.WriteAsync(": ping\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }

    /// <summary>Marker type so <c>ILogger&lt;&gt;</c> generic works with a non-static class.</summary>
    public sealed class EventStreamEndpointMarker;
}
