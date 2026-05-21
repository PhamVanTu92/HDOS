using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ReportingPlatform.Telemetry;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Telemetry
// ------------------------------------------------------------------

builder.Services.AddPlatformTelemetry(builder.Configuration, "Gateway");

// ------------------------------------------------------------------
// JWT authentication — validates all tokens at the gateway boundary.
// Backends re-validate the JWT themselves; gateway rejection is a
// first-line defense, not a replacement for backend validation (§5.3).
// Backends MUST use JWT claims for identity — never X-Tenant-Id (Patch 3, §8.3.1).
// ------------------------------------------------------------------

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        // MetadataAddress trỏ thẳng vào Keycloak internal (HTTP) để tránh vấn đề
        // SSL cert tự ký khi fetch discovery doc từ public HTTPS URL.
        var authority = builder.Configuration["Auth:Authority"]!;   // http://keycloak:8080/realms/hdos
        var publicIssuer = builder.Configuration["Auth:PublicIssuer"] ?? authority;

        opts.MetadataAddress      = $"{authority}/.well-known/openid-configuration";
        opts.Authority            = authority;
        opts.Audience             = builder.Configuration["Auth:Audience"];
        opts.RequireHttpsMetadata = false;   // Keycloak internal chạy HTTP
        opts.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            // Chấp nhận cả internal URL và public HTTPS URL làm issuer.
            // Keycloak trả iss = public URL (KC_HOSTNAME), còn Authority = internal URL.
            ValidIssuers = [authority, publicIssuer],
            ValidAudience = builder.Configuration["Auth:Audience"],
            ValidateLifetime = true,
            ClockSkew = System.TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(opts =>
{
    // YARP reserves the policy name "default" — use "Authenticated" instead.
    opts.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
});

// ------------------------------------------------------------------
// CORS — global config, per-environment origin allowlist (§7)
// AllowCredentials() required for SignalR WebSocket auth.
// Backends set AllowAnyOrigin() but are not directly reachable.
// ------------------------------------------------------------------

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(opts =>
{
    opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders("Content-Type", "X-Request-Id")
              .SetPreflightMaxAge(TimeSpan.FromHours(1)));
});

// ------------------------------------------------------------------
// Rate limiting (§5.4)
// Tier 1 — per IP (anti-flood): 10 000 req/min
// Tier 2 — per tenant: 5 000 req/min (cross-service aggregate)
// ------------------------------------------------------------------

builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = 429;

    opts.AddPolicy("GlobalIp", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit          = 10_000,
                Window               = TimeSpan.FromMinutes(1),
                SegmentsPerWindow    = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            }));

    opts.AddPolicy("PerTenant", ctx =>
    {
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value
                    ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? "anonymous";
        return RateLimitPartition.GetSlidingWindowLimiter(tenantId,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit          = 5_000,
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
            message = "Too many requests.",
        }, ct);
    };
});

// ------------------------------------------------------------------
// YARP reverse proxy — loaded from appsettings.json ReverseProxy section
// ------------------------------------------------------------------

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        // Forward validated JWT claims as informational headers (§5.3).
        // Backends MUST NOT use these headers for authorization — JWT only.
        ctx.AddRequestTransform(async reqCtx =>
        {
            var user = reqCtx.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var tenantId = user.FindFirstValue("tenant_id")
                            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
                var userId   = user.FindFirstValue(ClaimTypes.NameIdentifier);
                var scope    = user.FindFirstValue("scope");

                if (tenantId is not null)
                    reqCtx.ProxyRequest.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId);
                if (userId is not null)
                    reqCtx.ProxyRequest.Headers.TryAddWithoutValidation("X-User-Id", userId);
                if (scope is not null)
                    reqCtx.ProxyRequest.Headers.TryAddWithoutValidation("X-Token-Scope", scope);
            }

            // SSE: disable response buffering for streaming routes.
            if (reqCtx.HttpContext.Request.Path.StartsWithSegments("/sse"))
                reqCtx.HttpContext.Response.Headers["X-Accel-Buffering"] = "no";

            await Task.CompletedTask;
        });
    });

// ------------------------------------------------------------------
// Health checks
// ------------------------------------------------------------------

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

// ------------------------------------------------------------------
// Build and map
// ------------------------------------------------------------------

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Apply both rate limit policies to all proxied routes.
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.UseSessionAffinity();
    proxyPipeline.UseLoadBalancing();
})
.RequireRateLimiting("GlobalIp")
.RequireRateLimiting("PerTenant");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .AllowAnonymous();

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    AllowCachingResponses = false,
}).AllowAnonymous();

app.Run();

// Expose Program in the assembly's root namespace so WebApplicationFactory<Program>
// can locate it from the Gateway.Tests project.
namespace ReportingPlatform.Gateway
{
    public partial class Program { }
}
