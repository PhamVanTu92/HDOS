namespace ReportingPlatform.ProgressDispatcher.Workers;

/// <summary>
/// Background service that polls the active-progress Redis Set (<c>rp:active-progress</c>),
/// reads new events from each request's Redis Stream (<c>rp:progress:{requestId}</c>),
/// and publishes each event as a JSON payload to the Redis pub/sub channel
/// <c>rp:sse-notify:{requestId}</c>.
/// <para>
/// Each <c>Request.Api</c> node subscribes to <c>rp:sse-notify:*</c> pattern and fans out
/// received messages to local <c>SseConnectionRegistry</c> entries — bridging the
/// pub/sub hop to in-process <c>Channel&lt;SseEvent&gt;</c> writers.
/// </para>
/// </summary>
public sealed class ProgressRelayWorker : BackgroundService
{
    // In-memory tracking of the last stream entry ID relayed per requestId.
    // Reset to "0-0" on restart (replays buffered events — SSE clients de-duplicate by percent).
    private readonly ConcurrentDictionary<string, string> _lastIds = new();

    private readonly IDatabase            _redis;
    private readonly ISubscriber          _pubSub;
    private readonly ProgressOptions      _opts;
    private readonly ILogger<ProgressRelayWorker> _logger;

    // Serializer-context anchor — we only pass the requestId string as the channel value;
    // events are already stored as raw fields in the Stream and are re-serialized here as
    // a simple JSON object so Request.Api can deserialize without additional context.
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ProgressRelayWorker(
        IDatabase redis,
        ISubscriber pubSub,
        IOptions<ProgressOptions> opts,
        ILogger<ProgressRelayWorker> logger)
    {
        _redis  = redis;
        _pubSub = pubSub;
        _opts   = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProgressRelayWorker started (pollIntervalMs={Interval})",
            _opts.StreamPollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProgressRelayWorker error during batch relay");
            }

            await Task.Delay(_opts.StreamPollIntervalMs, stoppingToken);
        }

        _logger.LogInformation("ProgressRelayWorker stopped");
    }

    private async Task RelayBatchAsync(CancellationToken ct)
    {
        // O(1) SMEMBERS on active-progress Set (typically < 100 members in production).
        var members = await _redis.SetMembersAsync(RedisKeys.ActiveProgress);
        if (members.Length == 0)
            return;

        foreach (var member in members)
        {
            var requestId = (string?)member;
            if (string.IsNullOrEmpty(requestId))
                continue;

            await RelayRequestAsync(requestId, ct);
        }
    }

    private async Task RelayRequestAsync(string requestId, CancellationToken ct)
    {
        var afterId   = _lastIds.GetOrAdd(requestId, "0-0");
        var streamKey = RedisKeys.ProgressStream(requestId);

        StreamEntry[] entries;
        try
        {
            entries = await _redis.StreamReadAsync(streamKey, afterId,
                count: _opts.MaxEventsPerBatch) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read stream for requestId={RequestId}", requestId);
            return;
        }

        if (entries.Length == 0)
            return;

        var channel = RedisChannel.Literal(RedisKeys.SseNotify(requestId));

        foreach (var entry in entries)
        {
            var payload = BuildPayloadJson(requestId, entry);
            try
            {
                await _pubSub.PublishAsync(channel, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to publish SSE-notify for requestId={RequestId} entryId={EntryId}",
                    requestId, entry.Id);
            }
        }

        // Advance cursor past the last delivered entry.
        _lastIds[requestId] = entries[^1].Id.ToString();

        _logger.LogDebug(
            "Relayed {Count} progress event(s) for requestId={RequestId}",
            entries.Length, requestId);
    }

    private static string BuildPayloadJson(string requestId, StreamEntry entry)
    {
        // Reconstruct a lightweight progress JSON from stream fields.
        // Format matches what Request.Api expects from pub/sub:
        // { "event":"progress", "requestId":"...", "percent":42, "message":"...", "tsUnixMs":... }
        var pct  = (string?)entry["pct"]  ?? "0";
        var msg  = (string?)entry["msg"]  ?? string.Empty;
        var ts   = (string?)entry["ts"]   ?? string.Empty;
        var step = (string?)entry["step"];

        var stepPart = string.IsNullOrEmpty(step)
            ? string.Empty
            : $",\"step\":\"{EscapeJson(step)}\"";

        return
            $"{{\"event\":\"progress\",\"requestId\":\"{requestId}\"," +
            $"\"percent\":{pct},\"message\":\"{EscapeJson(msg)}\",\"tsUnixMs\":{ts}{stepPart}}}";
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
