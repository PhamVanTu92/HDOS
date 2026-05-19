using ReportingPlatform.Adapters.Factory;
using ReportingPlatform.Adapters.Implementations;

namespace ReportingPlatform.Adapters.Extensions;

public static class AdaptersExtensions
{
    /// <summary>
    /// Registers adapter services: three SQL adapters + the factory.
    /// Prerequisites (must be registered by the host before calling this):
    ///   - <see cref="SqlKataQueryBuilder"/> (from AddPlatformQueryBuilder)
    ///   - <see cref="NpgsqlDataSource"/> keyed or registered for raw SQL access
    /// </summary>
    public static IServiceCollection AddPlatformAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Raw/Timescale adapters share a data-plane NpgsqlDataSource.
        // If the caller hasn't configured a separate one, fall back to Database:Data.
        var connStr = configuration["Database:Data"]
            ?? configuration.GetConnectionString("Data")
            ?? throw new InvalidOperationException(
                "Data connection string not configured. " +
                "Set 'Database:Data' or 'ConnectionStrings:Data'.");

        var dataSource = NpgsqlDataSource.Create(connStr);

        services.AddSingleton<SqlQueryBuilderAdapter>();
        services.AddSingleton<SqlRawAdapter>(sp =>
            new SqlRawAdapter(
                dataSource,
                sp.GetRequiredService<ILogger<SqlRawAdapter>>()));
        services.AddSingleton<TimescaleAdapter>(sp =>
            new TimescaleAdapter(
                dataSource,
                sp.GetRequiredService<ILogger<TimescaleAdapter>>()));
        services.AddSingleton<DatasourceAdapterFactory>();

        return services;
    }
}
