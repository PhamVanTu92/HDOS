using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using ReportingPlatform.Adapters.Extensions;
using ReportingPlatform.Caching;
using ReportingPlatform.Metadata.Extensions;
using ReportingPlatform.Operations.Dispatcher;
using ReportingPlatform.Operations.Extensions;
using ReportingPlatform.Providers.Extensions;
using ReportingPlatform.QueryBuilder.Extensions;
using ReportingPlatform.Resolver.Extensions;
using ReportingPlatform.Router.Consumers;
using ReportingPlatform.Router.Options;
using ReportingPlatform.Telemetry;
using ReportingPlatform.Transformers.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Configuration
// ------------------------------------------------------------------

var routerOpts = builder.Configuration
    .GetSection(RouterOptions.Section)
    .Get<RouterOptions>() ?? new RouterOptions();

builder.Services.Configure<RouterOptions>(
    builder.Configuration.GetSection(RouterOptions.Section));

var rabbitUri    = builder.Configuration.GetConnectionString("RabbitMQ")
    ?? "amqp://guest:guest@localhost:5672";
var pgConnStr    = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=hdos;Username=hdos;Password=hdos";
var redisConnStr = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";

// ------------------------------------------------------------------
// Telemetry
// ------------------------------------------------------------------

builder.Services.AddPlatformTelemetry(builder.Configuration, "Operation.Router.Worker");

// ------------------------------------------------------------------
// Operations (handlers + dispatcher + progress buffer + idempotency)
// ------------------------------------------------------------------

builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnStr);
builder.Services.AddPlatformCaching(builder.Configuration);

// Full platform stack — required because OperationRequestConsumer dispatches to
// OperationDispatcher which executes all registered IOperationHandler implementations.
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(pgConnStr));
builder.Services.AddPlatformMetadata();
builder.Services.AddPlatformQueryBuilder(builder.Configuration);
builder.Services.AddPlatformTransformers();
builder.Services.AddPlatformAdapters(builder.Configuration);
builder.Services.AddPlatformResolver(builder.Configuration);
builder.Services.AddPlatformProvidersWithExistingDataSource();
builder.Services.AddPlatformOperations();

// ------------------------------------------------------------------
// MassTransit with RabbitMQ — three priority queues + health check
// ------------------------------------------------------------------

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OperationRequestConsumer>();

    // Health check: MassTransit 8 registers its own IHealthCheck automatically.
    // ConfigureHealthCheckOptions names it "rabbitmq" with the "ready" tag so the
    // /healthz/ready endpoint includes it.
    x.ConfigureHealthCheckOptions(opts =>
    {
        opts.Name = "rabbitmq";
        opts.Tags.Add("ready");
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(new Uri(rabbitUri));

        // Dead-letter exchange — declared once; queues route to it on TTL or rejection.
        cfg.Message<OperationRequestMessage>(m =>
            m.SetEntityName("operation.request"));

        var queues = new[]
        {
            ("op-request-high",   "operation.request.high"),
            ("op-request-normal", "operation.request.normal"),
            ("op-request-low",    "operation.request.low"),
        };

        foreach (var (queue, routingKey) in queues)
        {
            cfg.ReceiveEndpoint(queue, ep =>
            {
                ep.PrefetchCount          = routerOpts.PrefetchCount;
                ep.ConcurrentMessageLimit = routerOpts.ConcurrentMessageLimit;
                ep.Durable                = true;
                ep.ConfigureConsumeTopology = false; // topology declared via SetEntityName above

                // Retry: 3 attempts, exponential back-off (1 s → 10 s).
                // Only transient infrastructure faults hit this; operation-level
                // failures return normally from DispatchAsync as Status=Failed/Timeout.
                ep.UseMessageRetry(r => r.Exponential(3,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(2)));

                // Dead-letter exchange after retry exhaustion
                ep.SetQueueArgument("x-dead-letter-exchange", "operation.request.dlq");
                ep.SetQueueArgument("x-message-ttl", routerOpts.MessageTtlMs);

                ep.ConfigureConsumer<OperationRequestConsumer>(ctx);
            });
        }
    });
});

// ------------------------------------------------------------------
// Health checks — /healthz/live and /healthz/ready
// ------------------------------------------------------------------

builder.Services.AddHealthChecks()
    .AddCheck("self",         () => HealthCheckResult.Healthy(),               tags: ["live"])
    .AddNpgSql(pgConnStr,     name: "postgres",                                tags: ["ready"])
    .AddRedis(redisConnStr,   name: "redis",                                   tags: ["ready"]);
// "rabbitmq" check is registered by MassTransit via ConfigureHealthCheckOptions above.

// ------------------------------------------------------------------
// Graceful shutdown
// ------------------------------------------------------------------

builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(routerOpts.ShutdownTimeoutSeconds));

// ------------------------------------------------------------------
// Build and map
// ------------------------------------------------------------------

var app = builder.Build();

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    AllowCachingResponses = false,
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready") || r.Name == "rabbitmq",
    AllowCachingResponses = false,
});

app.Run();

// Partial class so Router.Tests can use WebApplicationFactory if needed in Phase 12.
public partial class Program { }
