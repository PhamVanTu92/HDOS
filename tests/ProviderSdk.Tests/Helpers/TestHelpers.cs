extern alias SdkAlias;

namespace ReportingPlatform.ProviderSdk.Tests.Helpers;

internal static class TestHelpers
{
    public static TokenManager BuildTokenManager(HttpMessageHandler handler, ProviderSdkOptions? opts = null)
    {
        var options = opts ?? DefaultOpts();
        var http = new HttpClient(handler) { BaseAddress = options.TokenEndpoint };
        return new TokenManager(http, options, NullLogger.Instance);
    }

    public static ProviderSdkOptions DefaultOpts(
        string tokenEndpoint = "http://fake-token/",
        string bridgeEndpoint = "http://fake-bridge:5000/") => new()
    {
        ProviderId     = "test-provider",
        ClientId       = "test-client",
        ClientSecret   = "test-secret",
        TokenEndpoint  = new Uri(tokenEndpoint),
        BridgeEndpoint = new Uri(bridgeEndpoint),
        ReconnectJitterFraction = 0.0,
    };

    public static ConnectionManager BuildConnectionManager(
        TokenManager tokenManager,
        HandlerRegistry registry,
        ProviderSdkOptions opts,
        SdkCallbacks callbacks,
        IDelay delay,
        IServiceProvider? sp = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builtSp = sp ?? services.BuildServiceProvider();
        return new ConnectionManager(
            tokenManager, registry, opts, builtSp,
            callbacks, delay,
            NullLogger<ConnectionManager>.Instance);
    }
}

/// <summary>IDelay implementation that records delays and returns instantly.</summary>
internal sealed class RecordingDelay : IDelay
{
    private readonly List<TimeSpan> _delays = new();
    public IReadOnlyList<TimeSpan> Delays => _delays;

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        _delays.Add(delay);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
