using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Metadata.Services;
using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Operations.Serialization;

namespace ReportingPlatform.Operations.Handlers.Metadata;

internal sealed class MetadataDashboardUpsertHandler : IOperationHandler
{
    public string OperationName => "metadata.dashboards.upsert";

    private readonly IDashboardMetadataRepository _repo;
    private readonly EventSubscriptionSyncService _subscriptionSync;
    private readonly ILogger<MetadataDashboardUpsertHandler> _logger;

    public MetadataDashboardUpsertHandler(
        IDashboardMetadataRepository repo,
        EventSubscriptionSyncService subscriptionSync,
        ILogger<MetadataDashboardUpsertHandler> logger)
    {
        _repo             = repo;
        _subscriptionSync = subscriptionSync;
        _logger           = logger;
    }

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<MetadataDashboardUpsertParams>(ParamsOpts)
            ?? throw new OperationException("INVALID_PARAMS", "definition is required.");

        var result = await _repo.UpsertAsync(context.TenantId, p.Definition, ct);

        // Keep event_subscriptions in sync with WidgetDefinition.SubscribesTo.
        // Non-fatal: log and continue on failure; the upsert itself already succeeded.
        try
        {
            await _subscriptionSync.SyncAsync(context.TenantId, p.Definition, ct);
        }
        catch (Exception ex)
        {
            // Log but don't surface to the caller — subscription sync failure is an
            // eventual-consistency concern, not a correctness failure for the upsert.
            _logger.LogError(ex,
                "EventSubscriptionSyncService failed for dashboard {DashboardCode}; subscriptions may be stale.",
                p.Definition.DashboardCode);
        }

        return JsonSerializer.SerializeToElement(result, OperationsJsonContext.Default.UpsertResult);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
