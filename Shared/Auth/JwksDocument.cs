namespace ReportingPlatform.Auth;

public sealed record JwksDocument
{
    [JsonPropertyName("keys")]
    public required IReadOnlyList<JwkEntry> Keys { get; init; }
}
