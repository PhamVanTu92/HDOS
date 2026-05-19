using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Metadata;

internal sealed class MetadataDatasourceDeleteHandler : IOperationHandler
{
    public string OperationName => "metadata.datasources.delete";

    private readonly IDatasourceMetadataRepository _repo;

    public MetadataDatasourceDeleteHandler(IDatasourceMetadataRepository repo) => _repo = repo;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<MetadataDatasourceDeleteParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "datasourceId is required.");

        var result = await _repo.DeleteAsync(context.TenantId, p.DatasourceId, ct);
        return JsonSerializer.SerializeToElement(result, OperationsJsonContext.Default.DeleteResult);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
