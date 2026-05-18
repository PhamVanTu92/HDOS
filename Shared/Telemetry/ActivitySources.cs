namespace ReportingPlatform.Telemetry;

public static class ActivitySources
{
    public static readonly ActivitySource Gateway        = new("ReportingPlatform.Gateway");
    public static readonly ActivitySource Resolver       = new("ReportingPlatform.Resolver");
    public static readonly ActivitySource Worker         = new("ReportingPlatform.Worker");
    public static readonly ActivitySource ProviderClient = new("ReportingPlatform.ProviderClient");
    public static readonly ActivitySource Cache          = new("ReportingPlatform.Cache");

    // All source names registered here so TelemetryExtensions can subscribe to all of them.
    internal static readonly string[] All =
    [
        Gateway.Name,
        Resolver.Name,
        Worker.Name,
        ProviderClient.Name,
        Cache.Name,
    ];
}
