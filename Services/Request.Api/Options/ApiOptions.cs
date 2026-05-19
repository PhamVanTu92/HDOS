namespace ReportingPlatform.RequestApi.Options;

public sealed class ApiOptions
{
    public const string Section = "Api";

    /// <summary>Per-user request limit per minute (HTTP path).</summary>
    public int PerUserPerMinute { get; init; } = 100;

    /// <summary>Per-tenant request limit per minute (HTTP path).</summary>
    public int PerTenantPerMinute { get; init; } = 500;
}
