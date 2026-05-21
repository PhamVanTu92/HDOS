using MassTransit;
using ReportingPlatform.Caching;
using ReportingPlatform.Telemetry;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
var hubOpts   = builder.Configuration.GetSection(RealtimeHubOptions.Section)
                    .Get<RealtimeHubOptions>() ?? new RealtimeHubOptions();
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var jwtAuth   = builder.Configuration["Auth:Authority"] ?? string.Empty;
var jwtAud    = builder.Configuration["Auth:Audience"]  ?? string.Empty;
var rabbitUri = builder.Configuration["RabbitMQ:Uri"]   ?? "amqp://guest:guest@localhost/";

// ── Shared infrastructure ─────────────────────────────────────────────────
builder.Services.AddPlatformCaching(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration, "Realtime.Hub");

// ── MassTransit (publish only — ICancelBus + IOperationBus need IPublishEndpoint) ──
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) => cfg.Host(new Uri(rabbitUri)));
});

// ── JWT authentication (query-param token for Hub negotiation) ───────────
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
                var token = ctx.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// ── SignalR + MessagePack + Redis backplane ───────────────────────────────
builder.Services
    .AddSignalR(o =>
    {
        o.EnableDetailedErrors = builder.Environment.IsDevelopment();
        o.AddFilter<HubExceptionFilter>();
    })
    .AddMessagePackProtocol()
    .AddStackExchangeRedis(redisConn, o =>
    {
        // Channel prefix isolates this deployment's hub traffic on shared Redis.
        o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("rp:hub");
    });

// ── Health checks ────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self",  () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddRedis(redisConn, name: "redis", tags: ["ready"]);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<MainHub>("/hubs/main").RequireAuthorization();

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

// Allow WebApplicationFactory introspection in Phase 12 integration tests.
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
