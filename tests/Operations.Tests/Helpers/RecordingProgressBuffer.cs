using ReportingPlatform.Contracts.Store;
using ReportingPlatform.Operations.Progress;

namespace ReportingPlatform.Operations.Tests.Helpers;

/// <summary>
/// In-memory implementation of <see cref="IProgressBuffer"/> for testing.
/// Records all appended events so tests can assert on them.
/// </summary>
internal sealed class RecordingProgressBuffer : IProgressBuffer
{
    private readonly List<ProgressEvent> _events = new();

    public IReadOnlyList<ProgressEvent> Events => _events;

    public Task AppendAsync(ProgressEvent evt, CancellationToken ct = default)
    {
        _events.Add(evt);
        return Task.CompletedTask;
    }
}
