using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Metadata.Results;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Dashboard;

internal sealed class DashboardListHandler : IOperationHandler
{
    public string OperationName => "dashboard.list";

    private readonly IDashboardMetadataRepository _repo;

    public DashboardListHandler(IDashboardMetadataRepository repo) => _repo = repo;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var summaries = await _repo.ListAsync(context.TenantId, ct);
        return JsonSerializer.SerializeToElement(
            summaries,
            OperationsJsonContext.Default.IReadOnlyListDashboardMetadataSummary);
    }
}
