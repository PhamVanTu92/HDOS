using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace ReportingPlatform.RequestApi.Services;

/// <summary>
/// BackgroundService that subscribes to the widget cache-invalidation pub/sub channel
/// published by <c>Event.Processor.Worker</c> (Option A — Patch 2).
///
/// Channel pattern: <c>rp:cache-invalidate:widget:{tenantId}:{dashCode}:{widgetId}</c>
///
/// On receipt, performs Redis SCAN+DEL on all L1 keys matching
/// <c>widget:{tenantId}:{dashCode}:v*:{widgetId}:*</c>.
///
/// L0 (in-process <see cref="IMemoryCache"/>) entries are NOT explicitly evicted;
/// they expire within 30 seconds via their promoted-entry TTL.
/// This 30-second staleness window is documented and accepted (OQ-P11-F / Patch 2).
/// </summary>
internal sealed class WidgetCacheInvalidationSubscriber : BackgroundService
{
    private const string ChannelPrefix = "rp:cache-invalidate:widget:";
    // Fixed segment count in the channel prefix:  rp : cache-invalidate : widget = 3 segments
    // Full channel: rp:cache-invalidate:widget:{tenantId}:{dashCode}:{widgetId}
    private const int ExpectedSegments = 6;

    private readonly IConnectionMultiplexer                   _mux;
    private readonly ILogger<WidgetCacheInvalidationSubscriber> _logger;

    public WidgetCacheInvalidationSubscriber(
        IConnectionMultiplexer mux,
        ILogger<WidgetCacheInvalidationSubscriber> logger)
    {
        _mux    = mux;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = _mux.GetSubscriber();

        await sub.SubscribeAsync(
            RedisChannel.Pattern($"{ChannelPrefix}*"),
            (channel, msg) =>
            {
                if (!TryParseChannel(channel, out var tenantId, out var dashCode, out var widgetId))
                {
                    _logger.LogWarning("Unparseable invalidation channel: {Channel}", channel.ToString());
                    return;
                }

                // Fire-and-forget: eviction is best-effort; Redis SCAN is async.
                // Suppress CS4014 — intentional unawaited task in sync callback.
#pragma warning disable CS4014
                EvictL1Async(tenantId!, dashCode!, widgetId!, stoppingToken);
#pragma warning restore CS4014
            });

        _logger.LogInformation("Widget cache invalidation subscriber active");

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);

        await sub.UnsubscribeAllAsync();
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static bool TryParseChannel(
        RedisChannel channel,
        out string? tenantId,
        out string? dashCode,
        out string? widgetId)
    {
        tenantId = dashCode = widgetId = null;

        var name = channel.ToString() ?? string.Empty;
        if (!name.StartsWith(ChannelPrefix, StringComparison.Ordinal))
            return false;

        // After prefix: {tenantId}:{dashCode}:{widgetId}
        var suffix   = name[ChannelPrefix.Length..];
        var segments = suffix.Split(':', 3, StringSplitOptions.None);
        if (segments.Length != 3)
            return false;

        tenantId = segments[0];
        dashCode = segments[1];
        widgetId = segments[2];

        return !string.IsNullOrEmpty(tenantId)
            && !string.IsNullOrEmpty(dashCode)
            && !string.IsNullOrEmpty(widgetId);
    }

    private async Task EvictL1Async(
        string tenantId, string dashCode, string widgetId,
        CancellationToken ct)
    {
        try
        {
            var db      = _mux.GetDatabase();
            var server  = _mux.GetEndPoints().FirstOrDefault();
            if (server is null)
                return;

            var srv     = _mux.GetServer(server);
            var pattern = $"widget:{tenantId}:{dashCode}:v*:{widgetId}:*";

            var keys = new List<RedisKey>();
            await foreach (var key in srv.KeysAsync(pattern: pattern).WithCancellation(ct))
                keys.Add(key);

            if (keys.Count > 0)
            {
                await db.KeyDeleteAsync(keys.ToArray());
                _logger.LogDebug(
                    "L1 cache evicted {Count} key(s) for widget {TenantId}/{DashCode}/{WidgetId}",
                    keys.Count, tenantId, dashCode, widgetId);
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — ignore.
        }
        catch (Exception ex)
        {
            // Redis failures are non-fatal — the widget data will be re-fetched on next request.
            _logger.LogWarning(ex,
                "L1 widget cache eviction failed for {TenantId}/{DashCode}/{WidgetId}",
                tenantId, dashCode, widgetId);
        }
    }
}
