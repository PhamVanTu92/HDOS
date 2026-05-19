using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Datasource;

internal sealed class DatasourceGetHandler : IOperationHandler
{
    public string OperationName => "datasource.get";

    private readonly IDatasourceMetadataRepository _repo;

    public DatasourceGetHandler(IDatasourceMetadataRepository repo) => _repo = repo;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<DatasourceGetParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "datasourceId is required.");

        var definition = await _repo.GetAsync(context.TenantId, p.DatasourceId, ct)
            ?? throw new OperationException("DATASOURCE_NOT_FOUND",
                $"Datasource '{p.DatasourceId}' not found for tenant '{context.TenantId}'.");

        return JsonSerializer.SerializeToElement(definition,
            OperationsJsonContext.Default.DatasourceDefinition);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
