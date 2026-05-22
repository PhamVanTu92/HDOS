using System.Threading.RateLimiting;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ReportingPlatform.Auth;
using ReportingPlatform.Contracts.Envelopes;
using ReportingPlatform.IngestionApi.Services;
using ReportingPlatform.Telemetry;

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

var defaultRateLimit = builder.Configuration.GetValue("Ingestion:RateLimits:Default", 1_000);

// ------------------------------------------------------------------
// Telemetry
// ------------------------------------------------------------------

builder.Services.AddPlatformTelemetry(builder.Configuration, "Ingestion.Api");

// ------------------------------------------------------------------
// JWT authentication (same JWKS as user auth — scope distinguishes token type)
// ------------------------------------------------------------------

var jwtAuth      = builder.Configuration["Auth:Authority"] ?? string.Empty;
var jwtAud       = builder.Configuration["Auth:Audience"]  ?? string.Empty;
var publicIssuer = builder.Configuration["Auth:PublicIssuer"] ?? jwtAuth;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.MetadataAddress      = $"{jwtAuth}/.well-known/openid-configuration";
        opts.Authority            = jwtAuth;
        opts.Audience             = jwtAud;
        opts.RequireHttpsMetadata = false;
        // Tokens minted via the public Keycloak URL carry the public issuer, but
        // signatures verify against the internal JWKS. Accept both issuers and
        // rewrite backchannel metadata/JWKS fetches to the internal hostname.
        opts.BackchannelHttpHandler = new KeycloakInternalHandler(publicIssuer);
        opts.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidIssuers     = [jwtAuth, publicIssuer],
            ValidAudience    = jwtAud,
            ValidateLifetime = true,
            ClockSkew        = TimeSpan.FromSeconds(30),
        };
    });

// Policy: require Bearer JWT granting the 'ingestion' scope. Keycloak emits the
// scope claim as a single space-separated string ("openid profile ... ingestion"),
// so check membership rather than an exact-value claim match.
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("IngestionScope", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
              {
                  var scopeClaim = ctx.User.FindFirst("scope")?.Value;
                  if (!string.IsNullOrEmpty(scopeClaim) &&
                      scopeClaim.Split(' ').Contains("ingestion"))
                      return true;
                  // Fallback: some setups split scope into individual claims.
                  return ctx.User.FindAll("scope").Any(c => c.Value == "ingestion");
              }));
});

// ------------------------------------------------------------------
// Rate limiting — sliding window per tenantId (§1.4)
// ------------------------------------------------------------------

builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = 429;

    opts.AddPolicy("PerTenant", ctx =>
    {
        // TenantId is always from JWT (Patch 3 invariant).
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value
                    ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? "anonymous";

        // Per-tenant limit configurable via appsettings.json → Ingestion:RateLimits:{tenantId}.
        var limit = builder.Configuration.GetValue($"Ingestion:RateLimits:{tenantId}", defaultRateLimit);

        return RateLimitPartition.GetSlidingWindowLimiter(tenantId, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit          = limit,
                Window               = TimeSpan.FromMinutes(1),
                SegmentsPerWindow    = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            });
    });

    opts.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            error   = "RATE_LIMIT_EXCEEDED",
            message = "Ingestion rate limit exceeded. Retry after 60 seconds.",
        }, ct);
    };
});

// ------------------------------------------------------------------
// Schema validation — IMemoryCache for compiled JsonSchema (§1.5.1 Patch 1)
// ------------------------------------------------------------------

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISchemaValidator>(sp =>
    new SchemaValidationService(
        pgConnStr,
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<ILogger<SchemaValidationService>>()));

// ------------------------------------------------------------------
// MassTransit — publish IngestEventEnvelope to events.raw topic exchange
// ------------------------------------------------------------------

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) =>
    {
        cfg.Host(new Uri(rabbitUri));

        // Publish IngestEventEnvelope to the "events.raw" TOPIC exchange. The
        // Event.Processor.Worker declares/binds events.raw as topic with routing key
        // "events.#", so the publisher MUST (a) match the exchange type — otherwise
        // RabbitMQ rejects with PRECONDITION_FAILED (fanout vs topic) and POST /events
        // hangs — and (b) send a routing key under "events." so the worker receives it.
        cfg.Message<IngestEventEnvelope>(m => m.SetEntityName("events.raw"));
        cfg.Publish<IngestEventEnvelope>(p => p.ExchangeType = "topic");
        cfg.Send<IngestEventEnvelope>(s => s.UseRoutingKeyFormatter(_ => "events.ingest"));
    });
});

// ------------------------------------------------------------------
// MVC controllers
// ------------------------------------------------------------------

builder.Services.AddControllers();

// ------------------------------------------------------------------
// Health checks
// ------------------------------------------------------------------

builder.Services.AddHealthChecks()
    .AddCheck("self",       () => HealthCheckResult.Healthy(),           tags: ["live"])
    .AddNpgSql(pgConnStr,  name: "postgres",                             tags: ["ready"])
    .AddRedis(redisConnStr, name: "redis",                               tags: ["ready"]);

// ------------------------------------------------------------------
// Build and map
// ------------------------------------------------------------------

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers()
   .RequireRateLimiting("PerTenant");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .AllowAnonymous();

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    AllowCachingResponses = false,
}).AllowAnonymous();

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    AllowCachingResponses = false,
}).AllowAnonymous();

app.Run();

// Rewrites backchannel OIDC/JWKS fetches from the public Keycloak host to the
// internal Docker hostname so metadata resolves on the platform network.
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

namespace ReportingPlatform.IngestionApi
{
    public partial class Program { }
}
