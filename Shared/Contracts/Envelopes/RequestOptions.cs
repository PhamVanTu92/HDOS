using ReportingPlatform.Contracts.Enums;

namespace ReportingPlatform.Contracts.Envelopes;

public sealed record RequestOptions
{
    public bool Progress { get; init; } = false;
    public int? CacheSeconds { get; init; }
    public Priority Priority { get; init; } = Priority.Normal;
    public int? TimeoutMs { get; init; }
}
