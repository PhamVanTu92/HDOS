namespace ReportingPlatform.ResponseDispatcher.Options;

public sealed class DispatcherOptions
{
    public const string Section = "Dispatcher";

    /// <summary>When true, fall back to user-level group push if connectionId is not found.</summary>
    public bool FallbackToUserGroup { get; init; } = true;

    /// <summary>MassTransit consumer prefetch count for operation-responses queue.</summary>
    public int PrefetchCount { get; init; } = 8;
}
