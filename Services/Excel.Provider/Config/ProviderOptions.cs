namespace ReportingPlatform.ExcelProvider.Config;

/// <summary>
/// Configuration options bound from the "Provider" section of appsettings.json.
/// </summary>
public sealed class ProviderOptions
{
    public const string SectionName = "Provider";

    /// <summary>Client ID used for platform token endpoint (client_credentials grant).</summary>
    public string ClientId { get; set; } = "excel-provider";

    /// <summary>Client secret (plain-text; BCrypt-hashed when seeding DB).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>URL of the platform token endpoint, e.g. http://request-api:5000/api/v1/providers/token</summary>
    public string TokenEndpoint { get; set; } = "http://localhost:5000/api/v1/providers/token";

    /// <summary>URL of the provider-bridge gRPC endpoint, e.g. http://provider-bridge:5400</summary>
    public string BridgeGrpcUrl { get; set; } = "http://localhost:5400";

    /// <summary>Provider ID string sent in the Hello message.</summary>
    public string ProviderId { get; set; } = "excel-provider";

    /// <summary>Semver of this provider.</summary>
    public string Version { get; set; } = "1.0.0";
}

/// <summary>
/// Configuration options bound from the "Excel" section of appsettings.json.
/// </summary>
public sealed class ExcelOptions
{
    public const string SectionName = "Excel";

    /// <summary>Directory containing SalesData.xlsx (and where it is generated if missing).</summary>
    public string DataPath { get; set; } = "./ExcelData";

    /// <summary>How long (in minutes) the in-memory Excel cache remains valid before reload.</summary>
    public int CacheMinutes { get; set; } = 5;
}
