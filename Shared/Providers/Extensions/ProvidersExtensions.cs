using Microsoft.Extensions.Hosting;
using ReportingPlatform.Providers.Registry;
using ReportingPlatform.Providers.Validation;

namespace ReportingPlatform.Providers.Extensions;

public static class ProvidersExtensions
{
    public static IServiceCollection AddPlatformProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Registry")
            ?? configuration["Database:Registry"]
            ?? throw new InvalidOperationException("Database:Registry connection string is not configured.");

        services.AddSingleton(NpgsqlDataSource.Create(connectionString));

        return AddPlatformProvidersCore(services);
    }

    /// <summary>
    /// Registers provider/operation registries using a pre-existing <see cref="NpgsqlDataSource"/>
    /// already present in the DI container. Callers are responsible for registering
    /// <see cref="NpgsqlDataSource"/> before calling this method.
    /// </summary>
    public static IServiceCollection AddPlatformProvidersWithExistingDataSource(
        this IServiceCollection services)
    {
        return AddPlatformProvidersCore(services);
    }

    private static IServiceCollection AddPlatformProvidersCore(IServiceCollection services)
    {
        services.AddSingleton<PostgresOperationRegistry>();
        services.AddSingleton<IOperationRegistry>(sp => sp.GetRequiredService<PostgresOperationRegistry>());

        services.AddSingleton<PostgresProviderRegistry>();
        services.AddSingleton<IProviderRegistry>(sp => sp.GetRequiredService<PostgresProviderRegistry>());

        services.AddSingleton<IParamsValidator, JsonSchemaParamsValidator>();

        services.AddHostedService<OperationRegistryRefreshService>();

        return services;
    }
}
