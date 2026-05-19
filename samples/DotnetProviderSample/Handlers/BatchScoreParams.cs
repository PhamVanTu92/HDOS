using System.Text.Json.Serialization;

namespace DotnetProviderSample.Handlers;

public sealed record BatchScoreParams
{
    [JsonPropertyName("transactions")]
    public required FraudScoreParams[] Transactions { get; init; }
}

public sealed record BatchScoreResult
{
    [JsonPropertyName("results")]      public required FraudScoreResult[] Results { get; init; }
    [JsonPropertyName("processed")]    public int Processed                       { get; init; }
    [JsonPropertyName("modelVersion")] public string ModelVersion { get; init; } = "mock-v1";
}
