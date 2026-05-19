using Microsoft.Extensions.DependencyInjection;

namespace ReportingPlatform.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddSigningKeyService(this IServiceCollection services)
    {
        services.AddSingleton<SigningKeyService>();
        services.AddSingleton<ISigningKeyService>(sp => sp.GetRequiredService<SigningKeyService>());
        services.AddHostedService(sp => sp.GetRequiredService<SigningKeyService>());
        services.AddSingleton<JwtIssuerService>();
        return services;
    }
}
