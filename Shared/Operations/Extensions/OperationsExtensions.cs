using ReportingPlatform.Contracts.Operations;
using ReportingPlatform.Metadata.Services;
using ReportingPlatform.Operations.Dispatcher;
using ReportingPlatform.Operations.Handlers.Admin;
using ReportingPlatform.Operations.Handlers.Dashboard;
using ReportingPlatform.Operations.Handlers.Datasource;
using ReportingPlatform.Operations.Handlers.Metadata;
using ReportingPlatform.Operations.Handlers.Widget;
using ReportingPlatform.Operations.Progress;
using ReportingPlatform.Operations.Services;

namespace ReportingPlatform.Operations.Extensions;

public static class OperationsExtensions
{
    public static IServiceCollection AddPlatformOperations(this IServiceCollection services)
    {
        // Progress buffer (wraps Redis-backed ring buffer)
        services.AddSingleton<IProgressBuffer, ProgressRingBufferAdapter>();

        // Idempotency (wraps IdempotencyStore)
        services.AddSingleton<IIdempotencyService, IdempotencyServiceAdapter>();

        // Bus adapters (wraps MassTransit IBus / IPublishEndpoint)
        services.AddSingleton<IOperationBus, MassTransitOperationBus>();
        services.AddSingleton<ICancelBus, MassTransitCancelBus>();

        // Registry + dispatcher (internal, but registered via public extension)
        services.AddSingleton<OperationHandlerRegistry>(sp =>
            new OperationHandlerRegistry(sp.GetServices<IOperationHandler>()));
        services.AddSingleton<OperationDispatcher>();
        services.AddSingleton<RequestSubmissionService>();
        services.AddSingleton<INestedRequestSubmitter>(sp =>
            sp.GetRequiredService<RequestSubmissionService>());

        // Shared services
        services.AddSingleton<FilterOptionsService>();
        services.AddSingleton<EventSubscriptionSyncService>();

        // Dashboard handlers
        services.AddSingleton<IOperationHandler, DashboardListHandler>();
        services.AddSingleton<IOperationHandler, DashboardGetHandler>();
        services.AddSingleton<IOperationHandler, DashboardRenderHandler>();

        // Widget handlers
        services.AddSingleton<IOperationHandler, WidgetRenderHandler>();
        services.AddSingleton<IOperationHandler, WidgetFilterOptionsHandler>();
        services.AddSingleton<IOperationHandler, WidgetTableExportHandler>();
        services.AddSingleton<IOperationHandler, WidgetDrillContextHandler>();

        // Datasource handlers
        services.AddSingleton<IOperationHandler, DatasourceListHandler>();
        services.AddSingleton<IOperationHandler, DatasourceGetHandler>();
        services.AddSingleton<IOperationHandler, DatasourcePreviewHandler>();

        // Metadata handlers
        services.AddSingleton<IOperationHandler, MetadataDashboardUpsertHandler>();
        services.AddSingleton<IOperationHandler, MetadataDashboardDeleteHandler>();
        services.AddSingleton<IOperationHandler, MetadataDatasourceUpsertHandler>();
        services.AddSingleton<IOperationHandler, MetadataDatasourceDeleteHandler>();
        services.AddSingleton<IOperationHandler, MetadataSchemaUpsertHandler>();

        // Admin handlers
        services.AddSingleton<IOperationHandler, AdminProvidersListHandler>();
        services.AddSingleton<IOperationHandler, AdminProvidersReloadHandler>();
        services.AddSingleton<IOperationHandler, AdminCacheFlushHandler>();

        return services;
    }
}
