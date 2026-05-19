namespace ReportingPlatform.ProgressDispatcher.Options;

public sealed class ProgressOptions
{
    public const string Section = "Progress";

    /// <summary>
    /// How often (in milliseconds) <see cref="ProgressRelayWorker"/> polls the
    /// active-progress Set and reads new stream entries.
    /// </summary>
    public int StreamPollIntervalMs { get; init; } = 5_000;

    /// <summary>Maximum Redis Stream entries read per request per poll cycle.</summary>
    public int MaxEventsPerBatch { get; init; } = 50;

    /// <summary>
    /// How often (in minutes) <see cref="ProgressReaperWorker"/> runs to purge
    /// stale entries from the active-progress Set.
    /// </summary>
    public int ReapIntervalMinutes { get; init; } = 10;
}
