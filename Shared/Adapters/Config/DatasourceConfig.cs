namespace ReportingPlatform.Adapters.Config;

/// <summary>
/// Parsed shape of <see cref="DatasourceDefinition.ConnectionConfig"/>
/// for datasources of type "sql".
/// </summary>
public sealed record DatasourceConfig
{
    // "querybuilder" | "raw" | "timescale"
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    // QueryBuilder mode: source name in the queryable_sources whitelist table.
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    // Raw / Timescale mode: parameterised SQL template.
    // Use @paramName placeholders — values are substituted from AdapterRequest.Filters.
    [JsonPropertyName("template")]
    public string? Template { get; init; }

    // Optional: override the registered database connection name.
    [JsonPropertyName("database")]
    public string? Database { get; init; }
}
