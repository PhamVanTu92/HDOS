using MassTransit;
using ReportingPlatform.Bridge.Interceptors;
using ReportingPlatform.Bridge.Resilience;

namespace ReportingPlatform.Bridge.Bridge;

public sealed class ProviderBridgeService : OperationProvider.OperationProviderBase
{
    private readonly IProviderRegistry           _registry;
    private readonly ProviderSessionManager      _sessionManager;
    private readonly ProviderResiliencePipeline  _resilience;
    private readonly IConnectionMultiplexer      _redis;
    private readonly RabbitMQ.Client.IConnection _rabbit;
    private readonly IPublishEndpoint            _publish;
    private readonly ILogger<ProviderBridgeService> _logger;

    public ProviderBridgeService(
        IProviderRegistry            registry,
        ProviderSessionManager       sessionManager,
        ProviderResiliencePipeline   resilience,
        IConnectionMultiplexer       redis,
        RabbitMQ.Client.IConnection  rabbit,
        IPublishEndpoint             publish,
        ILogger<ProviderBridgeService> logger)
    {
        _registry       = registry;
        _sessionManager = sessionManager;
        _resilience     = resilience;
        _redis          = redis;
        _rabbit         = rabbit;
        _publish        = publish;
        _logger         = logger;
    }

    public override async Task Connect(
        IAsyncStreamReader<FromProvider>  requestStream,
        IServerStreamWriter<ToProvider>   responseStream,
        ServerCallContext                  context)
    {
        if (!context.UserState.TryGetValue("claims", out var claimsObj)
            || claimsObj is not System.Security.Claims.ClaimsPrincipal claims)
        {
            throw new RpcException(new GrpcStatus(StatusCode.Unauthenticated, "UNAUTHENTICATED"));
        }

        var jwtSub   = claims.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        var provider = await _registry.GetAsync(jwtSub, context.CancellationToken);
        if (provider is null)
        {
            throw new RpcException(new GrpcStatus(StatusCode.Unauthenticated, "Provider not found"));
        }

        var sessionId = Guid.CreateVersion7().ToString();
        var session   = new ProviderSession(
            sessionId, requestStream, responseStream, context,
            provider, _resilience, _sessionManager,
            _redis, _rabbit,
            msg => _publish.Publish(msg, context.CancellationToken),
            _logger);

        await session.RunAsync(claims, context.CancellationToken);
    }
}
