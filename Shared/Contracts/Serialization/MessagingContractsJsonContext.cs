using ReportingPlatform.Contracts.Definitions;
using ReportingPlatform.Contracts.Enums;
using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.Contracts.Messaging;
using ReportingPlatform.Contracts.Store;
using ReportingPlatform.Contracts.TableParams;

namespace ReportingPlatform.Contracts.Serialization;

// MassTransit queue message contracts, Redis store records, and definition types.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Queue messages
[JsonSerializable(typeof(OperationRequestMessage))]
[JsonSerializable(typeof(OperationResponseMessage))]
[JsonSerializable(typeof(OperationProgressMessage))]
[JsonSerializable(typeof(CancelRequestMessage))]
[JsonSerializable(typeof(IngestEventEnvelope))]
// Redis store records
[JsonSerializable(typeof(OwnerStoreRecord))]
[JsonSerializable(typeof(ResultStoreRecord))]
[JsonSerializable(typeof(IdempotencyRecord))]
[JsonSerializable(typeof(ProgressEvent))]
// Table operation params (serialized into ParamsJson on request messages)
[JsonSerializable(typeof(TablePaginationParams))]
[JsonSerializable(typeof(SortSpec))]
[JsonSerializable(typeof(FilterSpec))]
[JsonSerializable(typeof(IReadOnlyList<SortSpec>))]
[JsonSerializable(typeof(IReadOnlyList<FilterSpec>))]
// Definition types (loaded from config / registry)
[JsonSerializable(typeof(DashboardDefinition))]
[JsonSerializable(typeof(DatasourceDefinition))]
[JsonSerializable(typeof(SchemaDefinition))]
// Enums
[JsonSerializable(typeof(Priority))]
[JsonSerializable(typeof(IdempotencyStatus))]
public partial class MessagingContractsJsonContext : JsonSerializerContext;
