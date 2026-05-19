using ReportingPlatform.Contracts.Store;

namespace ReportingPlatform.Operations.Progress;

/// <summary>
/// Abstraction over the Redis-backed progress ring buffer.
/// Used by <see cref="ProgressReporter"/> so tests can inject an in-memory recorder.
/// </summary>
public interface IProgressBuffer
{
    Task AppendAsync(ProgressEvent evt, CancellationToken ct = default);
}
