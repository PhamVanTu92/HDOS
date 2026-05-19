using ReportingPlatform.Operations.Serialization;
using ReportingPlatform.Providers.Abstractions;

namespace ReportingPlatform.Operations.Handlers.Admin;

internal sealed class AdminProvidersListHandler : IOperationHandler
{
    public string OperationName => "admin.providers.list";

    private readonly IOperationRegistry _registry;

    public AdminProvidersListHandler(IOperationRegistry registry) => _registry = registry;

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        var operations = await _registry.GetAllActiveAsync(ct);

        // Return a flat list of operation names + metadata; provider details in Phase 8
        var result = operations.Select(o => new
        {
            OperationPattern   = o.OperationPattern,
            HandlerType        = o.HandlerType,
            ProviderId         = o.ProviderId,
            Status             = o.Status,
            TimeoutMs          = o.TimeoutMs,
            Cacheable          = o.Cacheable,
            RequiredRole       = o.RequiredRole,
        }).ToList();

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        return JsonDocument.Parse(json).RootElement;
    }
}
