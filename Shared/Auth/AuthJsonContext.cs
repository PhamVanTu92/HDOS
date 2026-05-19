namespace ReportingPlatform.Auth;

[JsonSerializable(typeof(JwksDocument))]
[JsonSerializable(typeof(JwkEntry))]
[JsonSerializable(typeof(IReadOnlyList<JwkEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AuthJsonContext : JsonSerializerContext { }
