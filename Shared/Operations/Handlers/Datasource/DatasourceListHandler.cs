using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Datasource;

internal sealed class DatasourceListHandler : IOperationHandler
{
    public string OperationName => "datasource.list";

    private readonly IDatasourceMetadataRepository _repo;

    public DatasourceListHandler(IDatasourceMetadataRepository repo) => _repo = repo;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var summaries = await _repo.ListAsync(context.TenantId, ct);
        return JsonSerializer.SerializeToElement(
            summaries,
            OperationsJsonContext.Default.IReadOnlyListDatasourceSummary);
    }
}
