using ReportingPlatform.Caching;
using ReportingPlatform.ResponseDispatcher.Consumers;
using ReportingPlatform.ResponseDispatcher.Services;
using ReportingPlatform.Telemetry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────
var dispatcherOpts = builder.Configuration.GetSection(DispatcherOptions.Section)
                        .Get<DispatcherOptions>() ?? new DispatcherOptions();
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var rabbitUri = builder.Configuration["RabbitMQ:Uri"]           ?? "amqp://guest:guest@localhost/";

// ── Shared infrastructure ─────────────────────────────────────────────────
builder.Services.AddPlatformCaching(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration, "Response.Dispatcher.Worker");
builder.Services.Configure<DispatcherOptions>(
    builder.Configuration.GetSection(DispatcherOptions.Section));

// ── SignalR + Redis backplane (pusher mode — no MapHub) ───────────────────
// Same Redis backplane channel prefix as Realtime.Hub — enables cross-process push.
builder.Services
    .AddSignalR()
    .AddMessagePackProtocol()
    .AddStackExchangeRedis(redisConn, o =>
    {
        o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("rp:hub");
    });

// ── Response routing service ──────────────────────────────────────────────
builder.Services.AddSingleton<ResponseRouter>();

// ── MassTransit — consume operation-responses and cancel-requests ─────────
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OperationResponseConsumer>();
    x.AddConsumer<CancelRequestConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(new Uri(rabbitUri));

        cfg.ReceiveEndpoint("reporting.operation-responses", ep =>
        {
            ep.PrefetchCount = dispatcherOpts.PrefetchCount;
            ep.Durable       = true;
            ep.UseMessageRetry(r => r.Exponential(3,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)));
            ep.ConfigureConsumer<OperationResponseConsumer>(ctx);
        });

        cfg.ReceiveEndpoint("reporting.cancel-requests", ep =>
        {
            ep.PrefetchCount = dispatcherOpts.PrefetchCount;
            ep.Durable       = true;
            ep.ConfigureConsumer<CancelRequestConsumer>(ctx);
        });
    });
});

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddRedis(redisConn, name: "redis", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/healthz/live",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("live"),
    });
app.MapHealthChecks("/healthz/ready",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("ready"),
    });

await app.RunAsync();

public partial class Program { }
