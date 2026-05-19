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

        services.AddSingleton<PostgresOperationRegistry>();
        services.AddSingleton<IOperationRegistry>(sp => sp.GetRequiredService<PostgresOperationRegistry>());

        services.AddSingleton<PostgresProviderRegistry>();
        services.AddSingleton<IProviderRegistry>(sp => sp.GetRequiredService<PostgresProviderRegistry>());

        services.AddSingleton<IParamsValidator, JsonSchemaParamsValidator>();

        services.AddHostedService<OperationRegistryRefreshService>();

        return services;
    }
}
