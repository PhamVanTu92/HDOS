namespace ReportingPlatform.Bridge.Services;

public sealed class RevocationSubscriber : IHostedService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ProviderSessionManager _sessions;
    private readonly ILogger<RevocationSubscriber> _logger;
    private ISubscriber? _subscriber;

    public RevocationSubscriber(
        IConnectionMultiplexer redis,
        ProviderSessionManager sessions,
        ILogger<RevocationSubscriber> logger)
    {
        _redis    = redis;
        _sessions = sessions;
        _logger   = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _subscriber = _redis.GetSubscriber();
        _subscriber.Subscribe(
            RedisChannel.Pattern("rp:provider:revoked:*"),
            (channel, _value) =>
            {
                var providerId = channel.ToString().Replace("rp:provider:revoked:", "");
                _logger.LogWarning("Credential revocation received for provider {ProviderId}", providerId);
                var _ = _sessions.CloseAllForProviderAsync(providerId, "credentials_revoked");
            });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _subscriber?.UnsubscribeAll();
        return Task.CompletedTask;
    }
}
