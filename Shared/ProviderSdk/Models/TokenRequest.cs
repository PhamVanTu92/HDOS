namespace ReportingPlatform.ProviderSdk.Models;

internal sealed record TokenRequest
{
    [JsonPropertyName("clientId")]     public required string ClientId     { get; init; }
    [JsonPropertyName("clientSecret")] public required string ClientSecret { get; init; }
    [JsonPropertyName("grantType")]    public string GrantType { get; init; } = "client_credentials";
}
