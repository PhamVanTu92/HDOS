using ReportingPlatform.Providers.Abstractions;

namespace ReportingPlatform.Operations.Handlers.Admin;

internal sealed class AdminProvidersReloadHandler : IOperationHandler
{
    public string OperationName => "admin.providers.reload";

    private readonly IOperationRegistry _operationRegistry;
    private readonly IProviderRegistry _providerRegistry;

    public AdminProvidersReloadHandler(
        IOperationRegistry operationRegistry,
        IProviderRegistry providerRegistry)
    {
        _operationRegistry = operationRegistry;
        _providerRegistry  = providerRegistry;
    }

    public async Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default)
    {
        await Task.WhenAll(
            _operationRegistry.ReloadAsync(ct),
            _providerRegistry.ReloadAsync(ct));

        var json = """{"reloaded":true}""";
        return JsonDocument.Parse(json).RootElement;
    }
}
