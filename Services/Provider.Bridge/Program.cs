using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using ReportingPlatform.Bridge.Bridge;
using ReportingPlatform.Bridge.Interceptors;
using ReportingPlatform.Bridge.Resilience;
using ReportingPlatform.Bridge.Services;
using ReportingPlatform.Providers.Extensions;
using ReportingPlatform.Telemetry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var rabbitUri = builder.Configuration["RabbitMQ:Uri"]          ?? "amqp://guest:guest@localhost/";
var pgConnStr = builder.Configuration.GetConnectionString("Postgres")
               ?? "Host=localhost;Database=hdos;Username=hdos;Password=hdos";

// ── Telemetry ─────────────────────────────────────────────────────────────
builder.Services.AddPlatformTelemetry(builder.Configuration, "Provider.Bridge");

// ── Redis ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<IDatabase>(sp =>
    sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

// ── Postgres (for provider registry) ─────────────────────────────────────
builder.Services.AddSingleton(_ => Npgsql.NpgsqlDataSource.Create(pgConnStr));

// ── Provider registry ─────────────────────────────────────────────────────
builder.Services.AddPlatformProvidersWithExistingDataSource();

// ── RabbitMQ raw client (per-session consumers) ───────────────────────────
builder.Services.AddSingleton<IConnection>(_ =>
{
    var factory = new ConnectionFactory { Uri = new Uri(rabbitUri) };
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// ── MassTransit (publish terminal results to response exchange) ───────────
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) => cfg.Host(new Uri(rabbitUri)));
});

// ── JWKS cache ────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<JwksCache>();
builder.Services.AddSingleton<JwksCache>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JwksCache>());

// ── Bridge services ───────────────────────────────────────────────────────
builder.Services.AddSingleton<ProviderSessionManager>();
builder.Services.AddSingleton<ProviderResiliencePipeline>();
builder.Services.AddSingleton<RevocationSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RevocationSubscriber>());

// ── gRPC ──────────────────────────────────────────────────────────────────
builder.Services.AddGrpc(opts =>
{
    opts.Interceptors.Add<JwtValidationInterceptor>();
    opts.MaxReceiveMessageSize = 4 * 1024 * 1024;
    opts.MaxSendMessageSize    = 4 * 1024 * 1024;
});
builder.Services.AddSingleton<JwtValidationInterceptor>();

// ── Kestrel ───────────────────────────────────────────────────────────────
builder.WebHost.UseKestrel(opts =>
{
    opts.ListenAnyIP(5400, listenOpts =>
    {
        if (builder.Environment.IsProduction())
        {
            var certPath     = builder.Configuration["Bridge:TlsCertPath"]     ?? string.Empty;
            var certPassword = builder.Configuration["Bridge:TlsCertPassword"] ?? string.Empty;
            listenOpts.UseHttps(certPath, certPassword);
        }
        listenOpts.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self",    () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddRedis(redisConn, name: "redis", tags: ["ready"])
    .AddRabbitMQ(sp => sp.GetRequiredService<IConnection>(), name: "rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.MapGrpcService<ProviderBridgeService>();

app.MapHealthChecks("/healthz/live",  new HealthCheckOptions { Predicate = r => r.Tags.Contains("live")  });
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

await app.RunAsync();

public partial class Program { }
