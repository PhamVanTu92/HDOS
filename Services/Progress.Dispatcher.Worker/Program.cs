using ReportingPlatform.Caching;
using ReportingPlatform.ProgressDispatcher.Options;
using ReportingPlatform.ProgressDispatcher.Workers;
using ReportingPlatform.Telemetry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

// ── Shared infrastructure ─────────────────────────────────────────────────
builder.Services.AddPlatformCaching(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration, "Progress.Dispatcher.Worker");
builder.Services.Configure<ProgressOptions>(
    builder.Configuration.GetSection(ProgressOptions.Section));

// ── Redis pub/sub subscriber (separate multiplexer connection for pub/sub) ─
// StackExchange.Redis recommends a dedicated multiplexer for pub/sub to avoid
// blocking the command multiplexer during high-volume subscriptions.
builder.Services.AddSingleton<ISubscriber>(sp =>
{
    var mux = sp.GetRequiredService<IConnectionMultiplexer>();
    return mux.GetSubscriber();
});

// ── Background workers ────────────────────────────────────────────────────
builder.Services.AddHostedService<ProgressRelayWorker>();
builder.Services.AddHostedService<ProgressReaperWorker>();

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
