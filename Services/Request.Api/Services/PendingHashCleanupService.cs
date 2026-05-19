using Npgsql;

namespace ReportingPlatform.RequestApi.Services;

/// <summary>
/// Clears stale pending_client_secret_hash rows from provider_registry every 5 minutes.
/// 5-minute buffer beyond pending_secret_expires_at prevents race with active verifications.
/// </summary>
public sealed class PendingHashCleanupService : BackgroundService
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PendingHashCleanupService> _logger;

    public PendingHashCleanupService(NpgsqlDataSource db, ILogger<PendingHashCleanupService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var conn = await _db.OpenConnectionAsync(ct);
                await using var cmd  = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE provider_registry
                    SET pending_client_secret_hash = NULL,
                        pending_secret_expires_at  = NULL
                    WHERE pending_secret_expires_at IS NOT NULL
                      AND pending_secret_expires_at < NOW() - INTERVAL '5 minutes'
                    """;
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows > 0)
                    _logger.LogInformation("Cleared {Count} stale pending credential hashes", rows);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Pending hash cleanup failed");
            }
        }
    }
}
