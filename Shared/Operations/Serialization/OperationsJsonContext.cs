using ReportingPlatform.Contracts.Definitions;
using ReportingPlatform.Contracts.RenderPayloads;
using ReportingPlatform.Contracts.RenderPayloads.Operations;
using ReportingPlatform.Contracts.RenderPayloads.Shared;
using ReportingPlatform.Contracts.Validation;
using ReportingPlatform.Metadata.Results;

namespace ReportingPlatform.Operations.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy        = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter      = true,
    DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UpsertResult))]
[JsonSerializable(typeof(DeleteResult))]
[JsonSerializable(typeof(DashboardMetadataSummary))]
[JsonSerializable(typeof(IReadOnlyList<DashboardMetadataSummary>))]
[JsonSerializable(typeof(DashboardDefinition))]
[JsonSerializable(typeof(DashboardRenderPayload))]
[JsonSerializable(typeof(DatasourceDefinition))]
[JsonSerializable(typeof(DatasourceSummary))]
[JsonSerializable(typeof(IReadOnlyList<DatasourceSummary>))]
[JsonSerializable(typeof(DatasourcePreviewPayload))]
[JsonSerializable(typeof(FilterOptionsResult))]
[JsonSerializable(typeof(IReadOnlyList<FilterOption>))]
[JsonSerializable(typeof(TableExportResult))]
[JsonSerializable(typeof(DrillContextResult))]
[JsonSerializable(typeof(SchemaDefinition))]
[JsonSerializable(typeof(IReadOnlyList<SchemaDefinition>))]
[JsonSerializable(typeof(ValidationError))]
[JsonSerializable(typeof(IReadOnlyList<ValidationError>))]
public partial class OperationsJsonContext : JsonSerializerContext;
