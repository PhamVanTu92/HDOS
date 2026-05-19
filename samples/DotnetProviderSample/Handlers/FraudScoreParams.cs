using System.Text.Json.Serialization;

namespace DotnetProviderSample.Handlers;

public sealed record FraudScoreParams
{
    [JsonPropertyName("transactionId")]    public required string TransactionId    { get; init; }
    [JsonPropertyName("amount")]           public double Amount                    { get; init; }
    [JsonPropertyName("merchantCategory")] public string MerchantCategory          { get; init; } = "";
    [JsonPropertyName("features")]         public Dictionary<string, object>? Features { get; init; }
}

public sealed record FraudScoreResult
{
    [JsonPropertyName("transactionId")] public required string TransactionId { get; init; }
    [JsonPropertyName("score")]         public double Score                  { get; init; }
    [JsonPropertyName("riskBand")]      public required string RiskBand      { get; init; }
    [JsonPropertyName("modelVersion")]  public string ModelVersion           { get; init; } = "mock-v1";
}
