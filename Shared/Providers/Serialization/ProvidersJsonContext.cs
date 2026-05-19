using System.Text.Json.Serialization;
using ReportingPlatform.Providers.Models;

namespace ReportingPlatform.Providers.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CircuitBreakerConfig))]
internal partial class ProvidersJsonContext : JsonSerializerContext;
