namespace ReportingPlatform.Adapters.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DatasourceConfig))]
[JsonSerializable(typeof(ExternalProviderConfig))]
internal sealed partial class AdaptersJsonContext : JsonSerializerContext;
