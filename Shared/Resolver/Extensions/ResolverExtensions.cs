using ReportingPlatform.Resolver.Core;
using ReportingPlatform.Resolver.Invalidation;
using ReportingPlatform.Resolver.Repository;
using ReportingPlatform.Resolver.Validation;

namespace ReportingPlatform.Resolver.Extensions;

public static class ResolverExtensions
{
    /// <summary>
    /// Registers resolver services: definition repository, validator, cache, core resolver,
    /// and the cache-invalidation background service.
    ///
    /// Prerequisites (must be registered by the host before calling this):
    ///   - <see cref="IConnectionMultiplexer"/> (Redis)
    ///   - <see cref="IMemoryCache"/> (added automatically via AddMemoryCache inside)
    ///   - <see cref="TransformerRegistry"/> (from AddPlatformTransformers)
    ///   - <see cref="DatasourceAdapterFactory"/> (from AddPlatformAdapters)
    ///   - <see cref="SqlKataQueryBuilder"/> (from AddPlatformQueryBuilder)
    ///   - <see cref="IQueryableSourceRepository"/> (from AddPlatformQueryBuilder)
    ///   - <see cref="IComputedColumnEngine"/> (from AddPlatformTransformers)
    /// </summary>
    public static IServiceCollection AddPlatformResolver(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Dashboard+datasource definition storage
        var connStr = configuration["Database:Definitions"]
            ?? configuration.GetConnectionString("Definitions")
            ?? configuration["Database:QueryBuilder"]
            ?? configuration.GetConnectionString("QueryBuilder")
            ?? throw new InvalidOperationException(
                "Definitions connection string not configured. " +
                "Set 'Database:Definitions' or reuse 'Database:QueryBuilder'.");

        var dataSource = NpgsqlDataSource.Create(connStr);

        services.AddMemoryCache();

        services.AddSingleton<IDashboardDefinitionRepository>(sp =>
            new PostgresDashboardDefinitionRepository(
                dataSource,
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<ILogger<PostgresDashboardDefinitionRepository>>()));

        services.Configure<ResolverOptions>(configuration.GetSection(ResolverOptions.Section));

        services.AddSingleton<WidgetCacheService>();
        services.AddSingleton<IWidgetDefinitionValidator, WidgetDefinitionValidator>();
        services.AddSingleton<IDashboardResolver, DashboardResolver>();

        services.AddHostedService<DashboardCacheInvalidationService>();

        return services;
    }
}
