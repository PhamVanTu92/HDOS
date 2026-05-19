namespace ReportingPlatform.Resolver.Options;

public sealed class ResolverOptions
{
    public const string Section = "Resolver";

    /// <summary>Maximum concurrent widget adapter+transform calls per render request.</summary>
    public int MaxConcurrentWidgets { get; set; } = 10;

    /// <summary>Per-widget timeout in milliseconds (overridden per widget via VisualConfig["timeoutMs"]).</summary>
    public int DefaultWidgetTimeoutMs { get; set; } = 30_000;

    /// <summary>Default widget result cache TTL when datasource has no CacheSeconds.</summary>
    public int DefaultCacheTtlSeconds { get; set; } = 60;
}
