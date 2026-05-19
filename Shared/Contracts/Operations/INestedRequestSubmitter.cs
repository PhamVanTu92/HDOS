using ReportingPlatform.Contracts.Envelopes;

namespace ReportingPlatform.Contracts.Operations;

/// <summary>
/// Abstraction over <c>RequestSubmissionService</c> used by <c>ExternalProviderAdapter</c>.
/// Placed in Contracts so Adapters can depend on it without referencing Operations (avoiding a cycle).
/// </summary>
public interface INestedRequestSubmitter
{
    Task<SubmitAck> SubmitAsync(RequestEnvelope envelope, string? connectionId, CancellationToken ct = default);
}
