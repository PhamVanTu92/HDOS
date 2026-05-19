using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Metadata;

internal sealed class MetadataDashboardDeleteHandler : IOperationHandler
{
    public string OperationName => "metadata.dashboards.delete";

    private readonly IDashboardMetadataRepository _repo;

    public MetadataDashboardDeleteHandler(IDashboardMetadataRepository repo) => _repo = repo;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<MetadataDashboardDeleteParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "dashboardCode is required.");

        var result = await _repo.DeleteAsync(context.TenantId, p.DashboardCode, ct);
        return JsonSerializer.SerializeToElement(result, OperationsJsonContext.Default.DeleteResult);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
