using ReportingPlatform.QueryBuilder.Builder;

namespace ReportingPlatform.QueryBuilder.Extensions;

public static class QueryBuilderExtensions
{
    /// <summary>
    /// Registers the QueryBuilder services: whitelist repository, SqlKata executor.
    /// Prerequisites (must be registered by the host before calling this):
    ///   - <see cref="IConnectionMultiplexer"/> (Redis — for cache invalidation)
    /// </summary>
    public static IServiceCollection AddPlatformQueryBuilder(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration["Database:QueryBuilder"]
            ?? configuration.GetConnectionString("QueryBuilder")
            ?? configuration.GetConnectionString("Postgres")   // shared-Postgres fallback
            ?? throw new InvalidOperationException(
                "QueryBuilder connection string not configured. " +
                "Set 'Database:QueryBuilder', 'ConnectionStrings:QueryBuilder', " +
                "or 'ConnectionStrings:Postgres'.");

        // Capture one shared NpgsqlDataSource — not added to the DI container directly
        // to avoid collision with other NpgsqlDataSource registrations (e.g. Providers).
        var dataSource = NpgsqlDataSource.Create(connStr);

        services.AddMemoryCache();

        services.AddSingleton<IQueryableSourceRepository>(sp =>
            new PostgresQueryableSourceRepository(
                dataSource,
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<ILogger<PostgresQueryableSourceRepository>>()));

        services.AddSingleton<SqlKataQueryBuilder>(sp =>
            new SqlKataQueryBuilder(
                dataSource,
                sp.GetRequiredService<IQueryableSourceRepository>(),
                sp.GetRequiredService<ILogger<SqlKataQueryBuilder>>()));

        return services;
    }
}
