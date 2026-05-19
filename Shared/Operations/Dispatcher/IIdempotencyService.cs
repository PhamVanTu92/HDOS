namespace ReportingPlatform.Operations.Dispatcher;

/// <summary>
/// Thin interface over idempotency claim checking for <see cref="RequestSubmissionService"/>.
/// Production implementation wraps <see cref="ReportingPlatform.Caching.IdempotencyStore"/>.
/// Test implementations use in-memory dictionaries.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Returns true if this is the first claim for <paramref name="requestId"/> (owner).
    /// Returns false if another caller already holds the slot (idempotent re-submission).
    /// </summary>
    Task<bool> TryClaimAsync(
        string tenantId,
        string requestId,
        TimeSpan ttl,
        CancellationToken ct = default);
}
