using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.EventProcessor.Consumers;
using ReportingPlatform.EventProcessor.Services;
using ReportingPlatform.HubContracts;
using ReportingPlatform.Metadata.Abstractions;
using ReportingPlatform.Metadata.Repositories;
using ReportingPlatform.Telemetry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Configuration
// ------------------------------------------------------------------

var rabbitUri    = builder.Configuration.GetConnectionString("RabbitMQ")
    ?? "amqp://guest:guest@localhost:5672";
var pgConnStr    = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=hdos;Username=hdos;Password=hdos";
var redisConnStr = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";

// ------------------------------------------------------------------
// Telemetry
// ------------------------------------------------------------------

builder.Services.AddPlatformTelemetry(builder.Configuration, "Event.Processor.Worker");

// ------------------------------------------------------------------
// Redis (for L1 cache invalidation pub/sub — Option A Patch 2)
// ------------------------------------------------------------------

// Use a factory lambda so the connection is deferred until first use;
// calling Connect() eagerly at registration time can fail if Redis DNS
// is not yet reachable at the exact moment Program.cs runs.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnStr));

// ------------------------------------------------------------------
// SignalR backplane (same Redis instance)
// ------------------------------------------------------------------

// AddSignalR already registers IHubContext<MainHub, IMainHubClient>, which this
// worker resolves to push WidgetStale via the Redis backplane (no live Hub needed).
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisConnStr)
    .AddMessagePackProtocol();

// ------------------------------------------------------------------
// Metadata — event subscription repository
// ------------------------------------------------------------------

// PostgresEventSubscriptionRepository takes NpgsqlDataSource (not a raw string).
builder.Services.AddSingleton<IEventSubscriptionRepository>(
    new PostgresEventSubscriptionRepository(NpgsqlDataSource.Create(pgConnStr)));

// ------------------------------------------------------------------
// Domain service
// ------------------------------------------------------------------

builder.Services.AddSingleton<EventProcessorService>();

// ------------------------------------------------------------------
// MassTransit — consume from events.raw topic exchange
// ------------------------------------------------------------------

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<IngestEventConsumer>();

    x.ConfigureHealthCheckOptions(opts =>
    {
        opts.Name = "rabbitmq";
        opts.Tags.Add("ready");
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(new Uri(rabbitUri));

        // NOTE: Do NOT call cfg.Message<IngestEventEnvelope>(m => m.SetEntityName("events.raw"))
        // here — that would declare events.raw as a fanout exchange (MassTransit default),
        // conflicting with the explicit topic exchange declared in ep.Bind below.

        cfg.ReceiveEndpoint("event-processor", ep =>
        {
            ep.PrefetchCount = 20;
            ep.Durable       = true;

            // Bind directly to the events.raw topic exchange (declared by Ingestion.Api).
            ep.Bind("events.raw", b =>
            {
                b.ExchangeType = "topic";
                b.RoutingKey   = "events.#";
            });

            ep.UseMessageRetry(r => r.Exponential(3,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2)));

            ep.ConfigureConsumer<IngestEventConsumer>(ctx);
        });
    });
});

// ------------------------------------------------------------------
// Health checks
// ------------------------------------------------------------------

builder.Services.AddHealthChecks()
    .AddCheck("self",       () => HealthCheckResult.Healthy(),           tags: ["live"])
    .AddNpgSql(pgConnStr,  name: "postgres",                             tags: ["ready"])
    .AddRedis(redisConnStr, name: "redis",                               tags: ["ready"]);

// ------------------------------------------------------------------
// Graceful shutdown
// ------------------------------------------------------------------

builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(30));

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

namespace ReportingPlatform.EventProcessor
{
    public partial class Program { }
}
