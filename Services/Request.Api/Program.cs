using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using ReportingPlatform.Auth;
using ReportingPlatform.Caching;
using ReportingPlatform.Adapters.Extensions;
using ReportingPlatform.Metadata.Extensions;
using ReportingPlatform.Operations.Extensions;
using ReportingPlatform.Providers.Extensions;
using ReportingPlatform.QueryBuilder.Extensions;
using ReportingPlatform.Resolver.Extensions;
using ReportingPlatform.Transformers.Extensions;
using ReportingPlatform.RequestApi.Controllers;
using ReportingPlatform.RequestApi.Services;
using ReportingPlatform.RequestApi.Sse;
using ReportingPlatform.Telemetry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────
var apiOpts   = builder.Configuration.GetSection(ReportingPlatform.RequestApi.Options.ApiOptions.Section)
                    .Get<ReportingPlatform.RequestApi.Options.ApiOptions>()
                    ?? new ReportingPlatform.RequestApi.Options.ApiOptions();
var redisConn = builder.Configuration["Redis:ConnectionString"]  ?? "localhost:6379";
var rabbitUri = builder.Configuration["RabbitMQ:Uri"]            ?? "amqp://guest:guest@localhost/";
var jwtAuth   = builder.Configuration["Auth:Authority"]           ?? string.Empty;
var jwtAud    = builder.Configuration["Auth:Audience"]            ?? string.Empty;
var pgConnStr = builder.Configuration.GetConnectionString("Postgres")
                ?? "Host=localhost;Database=hdos;Username=hdos;Password=hdos";

// ── Shared infrastructure ─────────────────────────────────────────────────
builder.Services.AddPlatformCaching(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration, "Request.Api");

// ── Postgres ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(pgConnStr));

// ── Data Protection — always persist to Redis ─────────────────────────────
// Keys must survive container restarts (ephemeral FS is lost on every restart).
// Persisting to Redis ensures the same key ring is used across restarts and
// replicas regardless of ASPNETCORE_ENVIRONMENT.
var redisForDp = ConnectionMultiplexer.Connect(redisConn);
builder.Services.AddDataProtection()
    .SetApplicationName("ReportingPlatform.RequestApi")
    .PersistKeysToStackExchangeRedis(redisForDp, "DataProtection-Keys");

// ── Metadata repositories (Dashboard, Datasource, Schema) ────────────────
// Must be registered before AddPlatformOperations() — handlers depend on these.
builder.Services.AddPlatformMetadata();

// ── Full query / adapter / transformer / resolver stack ───────────────────
// All required by operation handlers registered in AddPlatformOperations().
// These extensions fall back to ConnectionStrings:Postgres if specific keys absent.
builder.Services.AddPlatformQueryBuilder(builder.Configuration);
builder.Services.AddPlatformTransformers();
builder.Services.AddPlatformAdapters(builder.Configuration);
builder.Services.AddPlatformResolver(builder.Configuration);

// ── Provider registry (uses the NpgsqlDataSource already registered above) ─
builder.Services.AddPlatformProvidersWithExistingDataSource();

// ── Signing key service + JWT issuer ──────────────────────────────────────
builder.Services.AddSigningKeyService();

// ── Provider-specific services ────────────────────────────────────────────
builder.Services.AddSingleton<ProviderLockoutService>(sp =>
    new ProviderLockoutService(sp.GetRequiredService<IDatabase>()));
builder.Services.AddHostedService<PendingHashCleanupService>();

// ── MassTransit (publish only) ────────────────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) => cfg.Host(new Uri(rabbitUri)));
});

// ── Platform operations (RequestSubmissionService, ICancelBus) ────────────
builder.Services.AddPlatformOperations();

// ── Request-Api-specific services ─────────────────────────────────────────
builder.Services.AddSingleton<SseConnectionRegistry>();
builder.Services.AddSingleton<OrphanDetector>();
builder.Services.AddHostedService<ProgressPubSubSubscriber>();
builder.Services.AddHostedService<WidgetCacheInvalidationSubscriber>();

// ── JWT authentication (user JWTs from external IdP) ─────────────────────
var publicIssuer = builder.Configuration["Auth:PublicIssuer"] ?? jwtAuth;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MetadataAddress      = $"{jwtAuth}/.well-known/openid-configuration";
        o.Authority            = jwtAuth;
        o.Audience             = jwtAud;
        o.RequireHttpsMetadata = false;
        o.BackchannelHttpHandler = new KeycloakInternalHandler(publicIssuer);
        o.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidIssuers  = [jwtAuth, publicIssuer],
            ValidAudience = jwtAud,
            ValidateLifetime = true,
            ClockSkew = System.TimeSpan.FromSeconds(30),
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.HttpContext.Request.Path.StartsWithSegments("/sse"))
                {
                    var token = ctx.Request.Query["access_token"].ToString();
                    if (!string.IsNullOrEmpty(token)) ctx.Token = token;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// ── Response caching (for JWKS endpoint) ─────────────────────────────────
builder.Services.AddResponseCaching();

// ── Rate limiting (per-user + per-tenant sliding window) ──────────────────
builder.Services.AddRateLimiter(o =>
{
    o.AddSlidingWindowLimiter("per-user", opts =>
    {
        opts.Window            = TimeSpan.FromMinutes(1);
        opts.PermitLimit       = apiOpts.PerUserPerMinute;
        opts.SegmentsPerWindow = 4;
        opts.QueueLimit        = 0;
    });
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = new { code = "RATE_LIMITED", retryAfterMs = 60_000 } }, ct);
    };
});

// ── MVC / controllers ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// ── Health checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddRedis(redisConn, name: "redis", tags: ["ready"])
    .AddNpgSql(pgConnStr, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.MapGet("/sse/requests/{requestId}/progress",
    [Authorize] async (
        string requestId,
        HttpContext ctx,
        SseConnectionRegistry registry,
        ProgressRingBuffer ringBuffer,
        ILoggerFactory loggers,
        CancellationToken ct) =>
    {
        await SseProgressEndpoint.HandleAsync(
            requestId, ctx, registry, ringBuffer,
            loggers.CreateLogger<SseProgressEndpoint.SseProgressEndpointMarker>(),
            ct);
    });

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

/// <summary>Rewrite public Keycloak HTTPS URL → internal Docker HTTP URL.</summary>
sealed file class KeycloakInternalHandler : HttpClientHandler
{
    private readonly string _externalHost;
    public KeycloakInternalHandler(string publicIssuerUrl)
    {
        _externalHost = new Uri(publicIssuerUrl).Host;
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host == _externalHost)
            request.RequestUri = new UriBuilder(request.RequestUri)
                { Scheme = "http", Host = "keycloak", Port = 8080 }.Uri;
        return base.SendAsync(request, ct);
    }
}
