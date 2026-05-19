using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Metadata;

internal sealed class MetadataSchemaUpsertHandler : IOperationHandler
{
    public string OperationName => "metadata.schemas.upsert";

    private readonly ISchemaMetadataRepository _repo;

    public MetadataSchemaUpsertHandler(ISchemaMetadataRepository repo) => _repo = repo;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<MetadataSchemaUpsertParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "definition is required.");

        var result = await _repo.UpsertAsync(context.TenantId, p.Definition, ct);
        return JsonSerializer.SerializeToElement(result, OperationsJsonContext.Default.UpsertResult);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
