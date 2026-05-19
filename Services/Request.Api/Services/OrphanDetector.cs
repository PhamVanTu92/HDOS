using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ReportingPlatform.RequestApi.Services;

/// <summary>
/// Determines whether a missing result is an orphan (submitted but result lost)
/// or simply an unknown requestId.
/// <para>
/// Checks the submission log key <c>rp:sublog:{requestId}</c> (TTL 30 min, written
/// by <see cref="RequestSubmissionService"/>). Presence → orphaned; absence → not_found.
/// </para>
/// </summary>
public sealed class OrphanDetector(IDatabase redis, ILogger<OrphanDetector> logger)
{
    public async Task<string> CheckAsync(string requestId, CancellationToken ct = default)
    {
        try
        {
            var exists = await redis.KeyExistsAsync(RedisKeys.SubmissionLog(requestId));
            return exists ? "orphaned" : "not_found";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Orphan check failed for requestId={RequestId}; defaulting to not_found",
                requestId);
            return "not_found";
        }
    }
}
