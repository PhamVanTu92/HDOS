namespace ReportingPlatform.ProviderSdk.Models;

internal sealed record TokenResponse
{
    [JsonPropertyName("accessToken")] public required string AccessToken { get; init; }
    [JsonPropertyName("expiresIn")]   public required int    ExpiresIn   { get; init; }
    [JsonPropertyName("tokenType")]   public string TokenType { get; init; } = "Bearer";
}
