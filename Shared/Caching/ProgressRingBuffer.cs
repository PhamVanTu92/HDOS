using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.Caching;

// Publishes progress events to a Redis Stream (ring buffer: maxlen=100, TTL=30s).
// SSE clients read from the stream using XREAD with the last-seen event ID.
public sealed class ProgressRingBuffer(IDatabase redis)
{
    private const int MaxLen = 100;
    private static readonly TimeSpan StreamTtl = TimeSpan.FromSeconds(30);

    public async Task AppendAsync(ProgressEvent evt, CancellationToken ct = default)
    {
        var key = RedisKeys.ProgressStream(evt.RequestId);

        var fields = new NameValueEntry[]
        {
            new("pct",  evt.Percent.ToString()),
            new("msg",  evt.Message),
            new("step", evt.Step ?? string.Empty),
            new("ts",   evt.Timestamp),
        };

        // XADD with MAXLEN ~ 100 (approximate trimming, O(1)).
        await redis.StreamAddAsync(key, fields, maxLength: MaxLen, useApproximateMaxLength: true);

        // Refresh TTL on every append so the stream lives as long as progress is flowing.
        await redis.KeyExpireAsync(key, StreamTtl);
    }

    // Returns events with IDs > afterId. Pass "0-0" to read all events from the start.
    public async Task<IReadOnlyList<ProgressEvent>> ReadAsync(
        string requestId,
        string afterId,
        CancellationToken ct = default)
    {
        var key     = RedisKeys.ProgressStream(requestId);
        var entries = await redis.StreamReadAsync(key, afterId);
        if (entries is null || entries.Length == 0)
            return [];

        return entries.Select(e => new ProgressEvent
        {
            RequestId = requestId,
            EventId   = e.Id.ToString(),
            Percent   = TryParseInt(e["pct"]),
            Message   = e["msg"].ToString(),
            Step      = e["step"].ToString() is { Length: > 0 } s ? s : null,
            Timestamp = e["ts"].ToString(),
        }).ToList();
    }

    private static int TryParseInt(RedisValue v) =>
        int.TryParse(v, out var n) ? n : 0;
}
