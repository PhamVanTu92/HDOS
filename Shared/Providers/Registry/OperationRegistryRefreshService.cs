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
        // Attempt to pre-warm both snapshots.  If the database is transiently
        // unavailable (e.g. container start-up race despite depends_on healthy
        // check), catch the error, log it, and schedule a background retry
        // rather than crashing the entire host.
        try
        {
            await _operationRegistry.ReloadAsync(cancellationToken);
            await _providerRegistry.ReloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Registry warm-up failed at startup — DB may not be ready yet. " +
                "Scheduling background retry; registry will be empty until reload succeeds.");
            _ = Task.Run(RetryLoadAsync, CancellationToken.None);
        }

        _subscriber = _redis.GetSubscriber();
        await _subscriber.SubscribeAsync(
            RedisChannel.Literal("operation-registry:updated"),
            OnOperationRegistryMessage);
    }

    /// <summary>
    /// Background retry loop: 5 s → 15 s → 30 s → 60 s, then gives up.
    /// </summary>
    private async Task RetryLoadAsync()
    {
        int[] delays = [5, 15, 30, 60];
        foreach (var delaySecs in delays)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySecs));
            try
            {
                await _operationRegistry.ReloadAsync(CancellationToken.None);
                await _providerRegistry.ReloadAsync(CancellationToken.None);
                _logger.LogInformation("Registry warm-up succeeded on background retry.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Registry warm-up retry failed (next delay: {NextDelay}s).", delaySecs * 2);
            }
        }
        _logger.LogError(
            "Registry warm-up failed after all retries. " +
            "Registry will remain empty until the next pub/sub hot-reload signal.");
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
