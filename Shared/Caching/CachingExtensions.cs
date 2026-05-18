using Microsoft.Extensions.Configuration;

namespace ReportingPlatform.Caching;

public static class CachingExtensions
{
    public static IServiceCollection AddPlatformCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379";

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(connectionString));

        services.AddSingleton<IDatabase>(sp =>
            sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        services.AddSingleton<OwnerStore>();
        services.AddSingleton<ResultStore>();
        services.AddSingleton<IdempotencyStore>();
        services.AddSingleton<ProgressRingBuffer>();
        services.AddSingleton<SingleFlightCoordinator>();

        return services;
    }
}
