namespace ReportingPlatform.ProgressDispatcher.Workers;

/// <summary>
/// Background service that periodically cross-checks the active-progress Redis Set
/// (<c>rp:active-progress</c>) against the submission-log key for each member.
/// Entries whose submission log has expired (TTL elapsed) are removed from the Set,
/// preventing stale members from accumulating after abnormal worker shutdown.
/// </summary>
public sealed class ProgressReaperWorker : BackgroundService
{
    private readonly IDatabase  _redis;
    private readonly ProgressOptions _opts;
    private readonly ILogger<ProgressReaperWorker> _logger;

    public ProgressReaperWorker(
        IDatabase redis,
        IOptions<ProgressOptions> opts,
        ILogger<ProgressReaperWorker> logger)
    {
        _redis  = redis;
        _opts   = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProgressReaperWorker started (intervalMin={Interval})",
            _opts.ReapIntervalMinutes);

        // Initial delay so the reaper doesn't run immediately on cold start.
        await Task.Delay(TimeSpan.FromMinutes(_opts.ReapIntervalMinutes), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReapStaleEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProgressReaperWorker error during reap pass");
            }

            await Task.Delay(TimeSpan.FromMinutes(_opts.ReapIntervalMinutes), stoppingToken);
        }

        _logger.LogInformation("ProgressReaperWorker stopped");
    }

    private async Task ReapStaleEntriesAsync(CancellationToken ct)
    {
        var members = await _redis.SetMembersAsync(RedisKeys.ActiveProgress);
        if (members.Length == 0)
            return;

        var reaped = 0;
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();

            var requestId = (string?)member;
            if (string.IsNullOrEmpty(requestId))
                continue;

            // If the submission log has expired, the request lifecycle is long over.
            // The ResponseRouter should have removed it from the Set, but in the case
            // of a crash or missed SREM this acts as a safety net.
            var logExists = await _redis.KeyExistsAsync(RedisKeys.SubmissionLog(requestId));
            if (!logExists)
            {
                await _redis.SetRemoveAsync(RedisKeys.ActiveProgress, requestId);
                reaped++;
                _logger.LogDebug("Reaped stale active-progress entry requestId={RequestId}", requestId);
            }
        }

        if (reaped > 0)
            _logger.LogInformation("Reaped {Count} stale active-progress entry(ies)", reaped);
    }
}
