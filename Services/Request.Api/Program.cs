using MassTransit;
using Microsoft.AspNetCore.RateLimiting;
using ReportingPlatform.Caching;
using ReportingPlatform.Operations.Extensions;
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
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var rabbitUri = builder.Configuration["RabbitMQ:Uri"]           ?? "amqp://guest:guest@localhost/";
var jwtAuth   = builder.Configuration["Auth:Authority"]          ?? string.Empty;
var jwtAud    = builder.Configuration["Auth:Audience"]           ?? string.Empty;

// ── Shared infrastructure ─────────────────────────────────────────────────
builder.Services.AddPlatformCaching(builder.Configuration);
builder.Services.AddPlatformTelemetry(builder.Configuration, "Request.Api");

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

// ── JWT authentication ────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = jwtAuth;
        o.Audience  = jwtAud;
        o.Events    = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                // Browser EventSource cannot set headers — accept ?access_token= for SSE.
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

// ── Rate limiting (per-user + per-tenant sliding window) ──────────────────
builder.Services.AddRateLimiter(o =>
{
    o.AddSlidingWindowLimiter("per-user", opts =>
    {
        opts.Window          = TimeSpan.FromMinutes(1);
        opts.PermitLimit     = apiOpts.PerUserPerMinute;
        opts.SegmentsPerWindow = 4;
        opts.QueueLimit      = 0;
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
    .AddRedis(redisConn, name: "redis", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// ── SSE endpoint (minimal API — controller doesn't fit the streaming response model) ──
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
