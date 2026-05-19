namespace ReportingPlatform.Adapters.Config;

/// <summary>
/// Deserialized shape of <see cref="DatasourceDefinition.ConnectionConfig"/>
/// for datasources of type "external_provider".
/// </summary>
public sealed record ExternalProviderConfig
{
    [JsonPropertyName("operationName")]
    public required string OperationName { get; init; }

    /// <summary>Optional routing hint for the Bridge; null = any active provider.</summary>
    [JsonPropertyName("providerId")]
    public string? ProviderId { get; init; }

    /// <summary>
    /// Maps provider param names to <c>{{filters.key}}</c> tokens or literal strings.
    /// Unknown tokens resolve to JSON null.
    /// </summary>
    [JsonPropertyName("paramMapping")]
    public required IReadOnlyDictionary<string, string> ParamMapping { get; init; }

    /// <summary>
    /// Dot-path into payload JSON to extract the rows array.
    /// Null = expect a "rows" property at the root of the payload.
    /// </summary>
    [JsonPropertyName("rowsPath")]
    public string? RowsPath { get; init; }

    /// <summary>Per-fetch timeout in ms. Default 5 000 ms. Hard cap enforced by adapter: 30 000 ms.</summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; } = 5_000;
}
