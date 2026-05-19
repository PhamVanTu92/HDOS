namespace ReportingPlatform.Adapters.Tests.Helpers;

/// <summary>
/// Captures submitted envelopes without touching a bus or Redis.
/// Invoke <see cref="OnSubmitAsync"/> to run side-effects after the capture
/// but before <see cref="SubmitAsync"/> returns — used by EP9 to trigger
/// the pub/sub notification synchronously while the adapter is still inside
/// the SubmitAsync call (before it awaits the TCS).
/// </summary>
internal sealed class FakeSubmissionService : INestedRequestSubmitter
{
    public RequestEnvelope? Captured { get; private set; }

    /// <summary>Optional callback executed after the envelope is captured.</summary>
    public Func<Task>? OnSubmitAsync { get; set; }

    /// <summary>When non-null, <see cref="SubmitAsync"/> throws this exception instead of succeeding.</summary>
    public Exception? ThrowOnSubmit { get; set; }

    public async Task<SubmitAck> SubmitAsync(
        RequestEnvelope envelope,
        string? connectionId,
        CancellationToken ct = default)
    {
        Captured = envelope;

        if (ThrowOnSubmit is not null)
            throw ThrowOnSubmit;

        if (OnSubmitAsync is not null)
            await OnSubmitAsync();

        return new SubmitAck
        {
            RequestId = envelope.RequestId,
            QueuedAt  = DateTimeOffset.UtcNow.ToString("O"),
        };
    }
}
