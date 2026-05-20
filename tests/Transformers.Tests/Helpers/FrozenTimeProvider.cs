namespace ReportingPlatform.Transformers.Tests.Helpers;

/// <summary>
/// A <see cref="TimeProvider"/> that always returns a fixed instant.
/// Use in tests that invoke code relying on <c>TimeProvider.GetUtcNow()</c>
/// to prevent golden-file drift or flakiness caused by real-clock reads.
/// </summary>
internal sealed class FrozenTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _frozen;

    public FrozenTimeProvider(DateTimeOffset frozen) => _frozen = frozen;

    public override DateTimeOffset GetUtcNow() => _frozen;
}
