using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ReportingPlatform.IngestionApi.Services;
using Xunit;

namespace ReportingPlatform.Ingestion.Tests.Integration;

// ─── TestAuthHandler ──────────────────────────────────────────────────────────

/// <summary>
/// Replaces JWT authentication in integration tests.
/// Set header <c>X-Test-Claims: claim1=value1,claim2=value2</c> to inject claims.
/// No header → authentication fails (anonymous).
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claimsHeader = Request.Headers["X-Test-Claims"].FirstOrDefault();
        if (claimsHeader is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = claimsHeader
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => new Claim(p[0].Trim(), p[1].Trim()))
            .ToList();

        claims.Add(new Claim(ClaimTypes.Name, "integration-test-user"));

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// ─── WebApplicationFactory ────────────────────────────────────────────────────

/// <summary>
/// Custom factory for Ingestion.Api integration tests.
/// Replaces JWT auth with <see cref="TestAuthHandler"/>, stubs MassTransit and
/// schema validation, and allows overriding the per-tenant rate limit.
/// </summary>
public sealed class IngestionApiFactory : WebApplicationFactory<ReportingPlatform.IngestionApi.Program>
{
    /// <summary>Per-tenant request rate limit to inject. Default 1 000 (production).</summary>
    public int RateLimitOverride { get; init; } = 1_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development environment → RequireHttpsMetadata=false
        builder.UseEnvironment("Development");

        // Override rate limit — must be set before host builds
        builder.UseSetting("Ingestion:RateLimits:Default", RateLimitOverride.ToString());

        // Disable OIDC metadata fetch (no real authority in tests)
        builder.UseSetting("Auth:Authority", "");

        builder.ConfigureTestServices(services =>
        {
            // ── 1. Replace authentication with TestAuthHandler ────────────
            // PostConfigure overrides the default scheme set in Program.cs.
            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultScheme             = TestAuthHandler.SchemeName;
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme    = TestAuthHandler.SchemeName;
                opts.DefaultForbidScheme       = TestAuthHandler.SchemeName;
            });
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // ── 2. Override IngestionScope policy to use TestAuthHandler ──
            services.PostConfigure<AuthorizationOptions>(opts =>
                opts.AddPolicy("IngestionScope", policy =>
                    policy.AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                          .RequireAuthenticatedUser()
                          .RequireClaim("scope", "ingestion")));

            // ── 3. Replace MassTransit bus startup (avoid RabbitMQ connection) ──
            var busHost = services.FirstOrDefault(d =>
                d.ImplementationType?.FullName?.Contains("MassTransitHostedService") == true);
            if (busHost is not null) services.Remove(busHost);

            // Replace IPublishEndpoint so controller can call Publish without a real bus
            var existing = services.Where(d => d.ServiceType == typeof(IPublishEndpoint)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddSingleton<IPublishEndpoint>(Substitute.For<IPublishEndpoint>());

            // ── 4. Replace schema validator with passthrough (no Postgres) ──
            var existingValidator = services
                .Where(d => d.ServiceType == typeof(ISchemaValidator))
                .ToList();
            foreach (var d in existingValidator) services.Remove(d);

            var passthrough = Substitute.For<ISchemaValidator>();
            passthrough
                .ValidateAsync(
                    Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<System.Text.Json.JsonElement>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<string?>(null));
            services.AddSingleton<ISchemaValidator>(passthrough);
        });
    }
}

// ─── Integration Tests ────────────────────────────────────────────────────────

/// <summary>IN4, IN9 — integration tests requiring WebApplicationFactory.</summary>
public sealed class EventIngestionIntegrationTests : IClassFixture<IngestionApiFactory>
{
    // Use a low-limit factory specifically for IN4.
    // IClassFixture doesn't allow constructor args, so IN4 creates its own factory.

    private static HttpContent MakeJsonBody() =>
        new StringContent(
            """{"eventType":"order.shipped","occurredAt":"2026-05-20T10:00:00Z","payload":{}}""",
            Encoding.UTF8,
            "application/json");

    // ─── IN4: Rate limit — exceed tenant quota → 429 + Retry-After ───────────

    [Fact]
    public async Task IN4_RateLimit_ExceedTenantQuota_Returns429_WithRetryAfter()
    {
        // Arrange — factory with rate limit of 2 req/min
        await using var factory = new IngestionApiFactory { RateLimitOverride = 2 };
        var client = factory.CreateClient();

        // Authenticated as tenant "rl-test" with ingestion scope
        client.DefaultRequestHeaders.Add("X-Test-Claims", "tenant_id=rl-test,scope=ingestion");

        // Act — send 5 requests (limit is 2)
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync(
                "/api/v1/events",
                new StringContent(
                    """{"eventType":"order.shipped","occurredAt":"2026-05-20T10:00:00Z","payload":{}}""",
                    Encoding.UTF8, "application/json")))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert — at least one 429 with Retry-After header
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.TooManyRequests);

        var throttled = responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(
            throttled.Headers.Contains("Retry-After"),
            "429 response must include Retry-After header");

        // Assert — the error body has the expected shape
        var body = await throttled.Content.ReadAsStringAsync();
        Assert.Contains("RATE_LIMIT_EXCEEDED", body);
    }

    // ─── IN9: JWT scope — missing ingestion scope → 403 ─────────────────────

    [Fact]
    public async Task IN9_JwtScope_MissingIngestionScope_Returns403()
    {
        // Arrange — authenticated user but WITHOUT scope=ingestion
        await using var factory = new IngestionApiFactory();
        var client = factory.CreateClient();

        // Provide tenant_id but no scope claim
        client.DefaultRequestHeaders.Add("X-Test-Claims", "tenant_id=test-tenant");

        // Act
        var response = await client.PostAsync(
            "/api/v1/events",
            new StringContent(
                """{"eventType":"order.shipped","occurredAt":"2026-05-20T10:00:00Z","payload":{}}""",
                Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
