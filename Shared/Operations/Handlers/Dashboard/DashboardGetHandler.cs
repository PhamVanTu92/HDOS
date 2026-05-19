using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Dashboard;

internal sealed class DashboardGetHandler : IOperationHandler
{
    public string OperationName => "dashboard.get";

    private readonly IDashboardMetadataRepository _repo;

    public DashboardGetHandler(IDashboardMetadataRepository repo) => _repo = repo;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = Deserialize<DashboardGetParams>(context.Params);

        var definition = await _repo.GetAsync(context.TenantId, p.DashboardCode, ct)
            ?? throw new OperationException("DASHBOARD_NOT_FOUND",
                $"Dashboard '{p.DashboardCode}' not found for tenant '{context.TenantId}'.");

        return JsonSerializer.SerializeToElement(definition,
            OperationsJsonContext.Default.DashboardDefinition);
    }

    private static T Deserialize<T>(JsonElement el) =>
        JsonSerializer.Deserialize<T>(el.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new OperationException("INVALID_PARAMS", $"Could not deserialize params as {typeof(T).Name}.");
}
