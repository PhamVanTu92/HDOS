using ReportingPlatform.Caching;
using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.Operations.Progress;

/// <summary>
/// Wraps the Redis-backed <see cref="ProgressRingBuffer"/> as <see cref="IProgressBuffer"/>.
/// Registered as the production implementation via DI.
/// </summary>
public sealed class ProgressRingBufferAdapter : IProgressBuffer
{
    private readonly ProgressRingBuffer _inner;

    public ProgressRingBufferAdapter(ProgressRingBuffer inner) => _inner = inner;

    public Task AppendAsync(ProgressEvent evt, CancellationToken ct = default) =>
        _inner.AppendAsync(evt, ct);
}
