using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.Contracts.Responses;
using ReportingPlatform.Contracts.Validation;

namespace ReportingPlatform.Contracts.Serialization;

// Client-facing HTTP API types: request envelopes, submit acks, response dispatch, progress, errors.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RequestEnvelope))]
[JsonSerializable(typeof(RequestOptions))]
[JsonSerializable(typeof(SubmitAck))]
[JsonSerializable(typeof(ResponseDispatchMessage))]
[JsonSerializable(typeof(ResponseProgressMessage))]
[JsonSerializable(typeof(ErrorDetail))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(ValidationError))]
[JsonSerializable(typeof(IReadOnlyList<ValidationError>))]
[JsonSerializable(typeof(ResponseStatus))]
[JsonSerializable(typeof(Priority))]
public partial class ClientContractsJsonContext : JsonSerializerContext;
