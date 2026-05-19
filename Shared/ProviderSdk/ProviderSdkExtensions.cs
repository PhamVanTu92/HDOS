namespace ReportingPlatform.ProviderSdk;

public static class ProviderSdkExtensions
{
    public static IProviderSdkBuilder AddProviderSdk(
        this IServiceCollection services,
        Action<ProviderSdkOptions> configure)
    {
        var opts = new ProviderSdkOptions
        {
            ProviderId     = string.Empty,
            ClientId       = string.Empty,
            ClientSecret   = string.Empty,
            TokenEndpoint  = new Uri("http://placeholder"),
            BridgeEndpoint = new Uri("http://placeholder"),
        };
        configure(opts);
        opts.Validate();

        var registry  = new Internal.HandlerRegistry();
        var callbacks = new Internal.SdkCallbacks();

        services.AddSingleton(opts);
        services.AddSingleton(registry);
        services.AddSingleton(callbacks);
        services.AddSingleton<Internal.IDelay, Internal.ProductionDelay>();
        services.AddSingleton<Internal.TokenManager>(sp =>
        {
            var http    = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ProviderSdk.Token");
            var logger  = sp.GetRequiredService<ILogger<Internal.TokenManager>>();
            return new Internal.TokenManager(http, opts, logger);
        });
        services.AddHttpClient("ProviderSdk.Token"); // uses default handler — caller can configure
        services.AddHostedService<Internal.ConnectionManager>();

        return new ProviderSdkBuilder(registry, callbacks);
    }
}
