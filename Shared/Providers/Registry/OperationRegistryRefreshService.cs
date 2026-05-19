using Microsoft.Extensions.Hosting;

namespace ReportingPlatform.Providers.Registry;

internal sealed class OperationRegistryRefreshService : IHostedService
{
    private readonly IOperationRegistry _operationRegistry;
    private readonly IProviderRegistry _providerRegistry;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OperationRegistryRefreshService> _logger;

    private ISubscriber? _subscriber;

    public OperationRegistryRefreshService(
        IOperationRegistry operationRegistry,
        IProviderRegistry providerRegistry,
        IConnectionMultiplexer redis,
        ILogger<OperationRegistryRefreshService> logger)
    {
        _operationRegistry = operationRegistry;
        _providerRegistry  = providerRegistry;
        _redis             = redis;
        _logger            = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Pre-warm both snapshots before accepting traffic.
        await _operationRegistry.ReloadAsync(cancellationToken);
        await _providerRegistry.ReloadAsync(cancellationToken);

        _subscriber = _redis.GetSubscriber();
        await _subscriber.SubscribeAsync(
            RedisChannel.Literal("operation-registry:updated"),
            OnOperationRegistryMessage);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
            await _subscriber.UnsubscribeAsync(RedisChannel.Literal("operation-registry:updated"));
    }

    private void OnOperationRegistryMessage(RedisChannel channel, RedisValue message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _operationRegistry.ReloadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Operation registry hot-reload failed on pub/sub notification");
            }
        });
    }
}
