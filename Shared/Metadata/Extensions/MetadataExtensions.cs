using ReportingPlatform.Metadata.Repositories;

namespace ReportingPlatform.Metadata.Extensions;

public static class MetadataExtensions
{
    public static IServiceCollection AddPlatformMetadata(this IServiceCollection services)
    {
        services.AddSingleton<IDashboardMetadataRepository, PostgresDashboardMetadataRepository>();
        services.AddSingleton<IDatasourceMetadataRepository, PostgresDatasourceMetadataRepository>();
        services.AddSingleton<ISchemaMetadataRepository, PostgresSchemaMetadataRepository>();
        services.AddSingleton<IEventSubscriptionRepository, PostgresEventSubscriptionRepository>();
        return services;
    }
}
