using ReportingPlatform.Caching;

namespace ReportingPlatform.Operations.Dispatcher;

/// <summary>
/// Wraps <see cref="IdempotencyStore"/> as <see cref="IIdempotencyService"/>.
/// Uses the requestId as both the operationKey and the requestId parameter.
/// </summary>
public sealed class IdempotencyServiceAdapter : IIdempotencyService
{
    private readonly IdempotencyStore _store;

    public IdempotencyServiceAdapter(IdempotencyStore store) => _store = store;

    public Task<bool> TryClaimAsync(
        string tenantId,
        string requestId,
        TimeSpan ttl,
        CancellationToken ct = default) =>
        _store.TryClaimAsync(tenantId, requestId, requestId, ttl, ct);
}
