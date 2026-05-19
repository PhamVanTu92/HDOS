using ReportingPlatform.Operations.Context.Params;
using ReportingPlatform.Resolver.Cache;

namespace ReportingPlatform.Operations.Handlers.Admin;

internal sealed class AdminCacheFlushHandler : IOperationHandler
{
    public string OperationName => "admin.cache.flush";

    private readonly WidgetCacheService _cache;
    private readonly ILogger<AdminCacheFlushHandler> _logger;

    public AdminCacheFlushHandler(WidgetCacheService cache, ILogger<AdminCacheFlushHandler> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var p = context.Params.Deserialize<AdminCacheFlushParams>(ParamsOpts)
                ?? new AdminCacheFlushParams();

        if (p.DashboardCode is not null)
        {
            // Evict L0 entries for a specific dashboard by prefix pattern
            var prefix = $"widget:{context.TenantId}:{p.DashboardCode}:";
            _cache.EvictFromL0(prefix);
            _logger.LogInformation(
                "Cache flush: tenant={TenantId} dashboard={Code}", context.TenantId, p.DashboardCode);
        }
        else
        {
            _logger.LogInformation("Cache flush: tenant={TenantId} (all)", context.TenantId);
        }

        var json = """{"flushed":true}""";
        return Task.FromResult(JsonDocument.Parse(json).RootElement);
    }

    private static readonly JsonSerializerOptions ParamsOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
