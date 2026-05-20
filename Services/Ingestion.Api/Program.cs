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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = builder.Configuration["Auth:Authority"];
        opts.Audience  = builder.Configuration["Auth:Audience"];
        opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

// Policy: require Bearer JWT with 'ingestion' scope.
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("IngestionScope", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("scope", "ingestion"));
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

        // Publish IngestEventEnvelope to the "events.raw" topic exchange.
        // Exchange type is declared as fanout (MassTransit default); the Event.Processor.Worker
        // consumer binds to it with "events.#" routing key.
        // Selective per-tenant routing keys are a Phase 12 enhancement.
        cfg.Message<IngestEventEnvelope>(m => m.SetEntityName("events.raw"));
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

namespace ReportingPlatform.IngestionApi
{
    public partial class Program { }
}
