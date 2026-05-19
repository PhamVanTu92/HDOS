namespace ReportingPlatform.ProviderSdk.Internal;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Models.TokenRequest))]
[JsonSerializable(typeof(Models.TokenResponse))]
internal sealed partial class SdkJsonContext : JsonSerializerContext { }
