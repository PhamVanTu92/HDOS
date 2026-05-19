using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ReportingPlatform.RequestApi.Sse;

/// <summary>
/// BackgroundService that subscribes to Redis pub/sub channels published by
/// <c>Progress.Dispatcher.Worker</c> and fans out events to local SSE connections
/// via <see cref="SseConnectionRegistry"/>.
/// <list type="bullet">
///   <item><c>rp:sse-notify:*</c> — new progress events</item>
///   <item><c>rp:sse-terminal:*</c> — terminal signals (operation complete)</item>
/// </list>
/// </summary>
public sealed class ProgressPubSubSubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer      _mux;
    private readonly SseConnectionRegistry       _registry;
    private readonly ILogger<ProgressPubSubSubscriber> _logger;

    public ProgressPubSubSubscriber(
        IConnectionMultiplexer mux,
        SseConnectionRegistry registry,
        ILogger<ProgressPubSubSubscriber> logger)
    {
        _mux      = mux;
        _registry = registry;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = _mux.GetSubscriber();

        // Subscribe to progress events from ProgressRelayWorker.
        await sub.SubscribeAsync(
            RedisChannel.Pattern("rp:sse-notify:*"),
            (channel, value) =>
            {
                var requestId = ExtractId(channel, "rp:sse-notify:");
                if (requestId is null || value.IsNullOrEmpty) return;

                // Value is the raw JSON progress data string.
                _registry.FanOut(requestId, new SseEvent("progress", value!));
            });

        // Subscribe to terminal signals from ResponseRouter.
        await sub.SubscribeAsync(
            RedisChannel.Pattern("rp:sse-terminal:*"),
            (channel, value) =>
            {
                var requestId = ExtractId(channel, "rp:sse-terminal:");
                if (requestId is null) return;

                var terminalJson = JsonSerializer.Serialize(new
                {
                    requestId,
                    resultUrl = $"/api/v1/requests/{requestId}/result",
                });
                _registry.FanOut(requestId, new SseEvent("terminal", terminalJson));
            });

        _logger.LogInformation("SSE pub/sub subscriber active");

        // Hold until host stops.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);

        await sub.UnsubscribeAllAsync();
    }

    private static string? ExtractId(RedisChannel channel, string prefix)
    {
        var name = channel.ToString();
        return name.StartsWith(prefix, StringComparison.Ordinal)
            ? name[prefix.Length..]
            : null;
    }
}
