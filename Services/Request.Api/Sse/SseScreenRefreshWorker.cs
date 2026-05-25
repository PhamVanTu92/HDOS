using System.Collections.Concurrent;
using System.Text.Json;
using Npgsql;
using StackExchange.Redis;

namespace ReportingPlatform.RequestApi.Sse;

/// <summary>
/// Background service that automatically pushes <c>WidgetStale</c> SSE events for all
/// published report screens configured with <c>refresh_mode = 'sse'</c>.
///
/// Each screen is refreshed at its own <c>refresh_interval_s</c> cadence
/// (default <see cref="DefaultIntervalSeconds"/> when the value is 0).
///
/// This makes SSE mode truly automatic — browsers watching the screen see chart data
/// update without any manual button press or page reload.
///
/// External systems (e.g. Excel Provider) can additionally call
/// <c>POST /api/v1/reports/screens/{id}/stale</c> for an out-of-band immediate push.
/// </summary>
public sealed class SseScreenRefreshWorker : BackgroundService
{
    private const int DefaultIntervalSeconds = 30;
    private const int TickDelayMs            = 5_000; // check every 5 s

    private readonly NpgsqlDataSource             _db;
    private readonly IConnectionMultiplexer        _redis;
    private readonly ILogger<SseScreenRefreshWorker> _logger;

    // Per-screen timestamp of the last stale push (in-memory, resets on restart).
    private readonly ConcurrentDictionary<Guid, DateTime> _lastPushed = new();

    public SseScreenRefreshWorker(
        NpgsqlDataSource db,
        IConnectionMultiplexer redis,
        ILogger<SseScreenRefreshWorker> logger)
    {
        _db     = db;
        _redis  = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SseScreenRefreshWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SseScreenRefreshWorker tick error — will retry next cycle");
            }

            await Task.Delay(TickDelayMs, stoppingToken);
        }

        _logger.LogInformation("SseScreenRefreshWorker stopped");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // ── Load all published SSE-mode screens ──────────────────────────────
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, refresh_interval_s
            FROM report_screens
            WHERE refresh_mode = 'sse'
              AND status       = 'published'
            """;

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var screens = new List<(Guid Id, int IntervalS)>();
        while (await rdr.ReadAsync(ct))
            screens.Add((rdr.GetGuid(0), rdr.GetInt32(1)));

        if (screens.Count == 0) return;

        // ── Push stale for screens whose interval has elapsed ────────────────
        var now    = DateTime.UtcNow;
        var redisDb = _redis.GetDatabase();

        foreach (var (screenId, intervalS) in screens)
        {
            var interval = TimeSpan.FromSeconds(intervalS > 0 ? intervalS : DefaultIntervalSeconds);
            var last     = _lastPushed.GetOrAdd(screenId, DateTime.MinValue);

            if (now - last < interval) continue;

            var channel = $"screen:{screenId}";
            var payload = JsonSerializer.Serialize(new
            {
                eventType = "WidgetStale",
                channel,
                reason    = "scheduled.refresh",
                updatedAt = now.ToString("O"),
            });

            await redisDb.PublishAsync(
                RedisChannel.Literal("rp:sse-global-event"),
                payload);

            _lastPushed[screenId] = now;
            _logger.LogDebug("SseScreenRefreshWorker → stale pushed for screen {ScreenId}", screenId);
        }
    }
}
