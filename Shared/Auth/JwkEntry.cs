namespace ReportingPlatform.Auth;

public sealed record JwkEntry
{
    [JsonPropertyName("kty")]
    public required string Kty { get; init; }

    [JsonPropertyName("use")]
    public required string Use { get; init; }

    [JsonPropertyName("alg")]
    public required string Alg { get; init; }

    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    [JsonPropertyName("n")]
    public required string N { get; init; }

    [JsonPropertyName("e")]
    public required string E { get; init; }
}
