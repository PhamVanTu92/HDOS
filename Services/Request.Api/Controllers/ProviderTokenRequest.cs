namespace ReportingPlatform.RequestApi.Controllers;

public sealed record ProviderTokenRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("clientSecret")]
    public required string ClientSecret { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("grantType")]
    public required string GrantType { get; init; }
}
