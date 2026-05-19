using ReportingPlatform.Contracts.TableParams;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;
using ReportingPlatform.Resolver.Abstractions;

namespace ReportingPlatform.Operations.Handlers.Dashboard;

internal sealed class DashboardRenderHandler : IOperationHandler
{
    public string OperationName => "dashboard.render";

    private readonly IDashboardResolver _resolver;

    public DashboardRenderHandler(IDashboardResolver resolver) => _resolver = resolver;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<DashboardRenderParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "dashboardCode is required.");

        if (string.IsNullOrWhiteSpace(p.DashboardCode))
            throw new OperationException("INVALID_PARAMS", "dashboardCode must be non-empty.");

        var filters     = p.Filters      ?? new Dictionary<string, JsonElement>();
        var tableParams = p.TableParams;

        context.Progress?.Report(new ProgressUpdate(10, "Starting dashboard render..."));

        var payload = await _resolver.RenderAsync(
            context.TenantId, p.DashboardCode, filters, tableParams, ct,
            callerRequestId: context.RequestId,
            callerUserId:    context.UserId,
            callerDeadline:  context.TimeoutAtUnixMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(context.TimeoutAtUnixMs)
                : null);

        context.Progress?.Report(new ProgressUpdate(100, "Dashboard render complete."));

        return JsonSerializer.SerializeToElement(payload,
            OperationsJsonContext.Default.DashboardRenderPayload);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
