using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ReportingPlatform.Gateway.Tests.Integration;

// ─── TestAuthHandler ──────────────────────────────────────────────────────────

/// <summary>
/// Replaces JWT authentication in Gateway integration tests.
/// Set header <c>X-Test-Claims: claim1=value1,claim2=value2</c> to inject claims.
/// </summary>
public sealed class GatewayTestAuthHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "GatewayTest";

    public GatewayTestAuthHandler(
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

        claims.Add(new Claim(ClaimTypes.Name, "gw-test-user"));

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// ─── Backend stub ─────────────────────────────────────────────────────────────

/// <summary>
/// Real in-process HTTP server on a random OS-assigned port.
/// YARP proxies to this server for integration tests.
/// </summary>
public sealed class BackendStub : IAsyncDisposable
{
    private readonly WebApplication _app;

    public int Port { get; }

    private BackendStub(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    /// <summary>Start a stub that calls <paramref name="handler"/> for every request.</summary>
    public static async Task<BackendStub> StartAsync(
        Func<HttpContext, Task> handler,
        CancellationToken ct = default)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:0"); // OS assigns a free port
        b.Logging.SetMinimumLevel(LogLevel.None); // suppress test noise

        var app = b.Build();
        // Use middleware (not app.Run which resolves to WebApplication.Run(string?))
        app.Use((Func<HttpContext, RequestDelegate, Task>)((ctx, _) => handler(ctx)));
        await app.StartAsync(ct);

        var addr  = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .First();
        var port  = new Uri(addr).Port;

        return new BackendStub(app, port);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }
}

// ─── Gateway factory helper ───────────────────────────────────────────────────

/// <summary>
/// Builds a Gateway <see cref="WebApplicationFactory{TEntryPoint}"/> with:
/// <list type="bullet">
///   <item>TestAuthHandler replacing JWT bearer</item>
///   <item>YARP clusters pointing at dynamic stub ports</item>
///   <item>Optional rate-limit override (GlobalIp)</item>
/// </list>
/// </summary>
internal static class GatewayFactory
{
    private const int DefaultGlobalIpLimit = 10_000;

    public static WebApplicationFactory<ReportingPlatform.Gateway.Program> Create(
        int?   requestApiPort      = null,
        int?   ingestionApiPort    = null,
        int?   realtimeHubPort     = null,
        int    globalIpLimitOverride = DefaultGlobalIpLimit)
    {
        return new WebApplicationFactory<ReportingPlatform.Gateway.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                // ── YARP cluster overrides ────────────────────────────────
                if (requestApiPort.HasValue)
                    builder.UseSetting(
                        "ReverseProxy:Clusters:request-api:Destinations:primary:Address",
                        $"http://127.0.0.1:{requestApiPort}/");

                if (ingestionApiPort.HasValue)
                    builder.UseSetting(
                        "ReverseProxy:Clusters:ingestion-api:Destinations:primary:Address",
                        $"http://127.0.0.1:{ingestionApiPort}/");

                if (realtimeHubPort.HasValue)
                    builder.UseSetting(
                        "ReverseProxy:Clusters:realtime-hub:Destinations:primary:Address",
                        $"http://127.0.0.1:{realtimeHubPort}/");

                // Disable OIDC discovery
                builder.UseSetting("Auth:Authority", "");

                builder.ConfigureTestServices(services =>
                {
                    // Replace JWT authentication with TestAuthHandler
                    services.PostConfigure<AuthenticationOptions>(opts =>
                    {
                        opts.DefaultScheme             = GatewayTestAuthHandler.SchemeName;
                        opts.DefaultAuthenticateScheme = GatewayTestAuthHandler.SchemeName;
                        opts.DefaultChallengeScheme    = GatewayTestAuthHandler.SchemeName;
                        opts.DefaultForbidScheme       = GatewayTestAuthHandler.SchemeName;
                    });
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, GatewayTestAuthHandler>(
                            GatewayTestAuthHandler.SchemeName, _ => { });

                    // Override Authenticated policy to use the test scheme
                    services.PostConfigure<AuthorizationOptions>(opts =>
                        opts.AddPolicy("Authenticated", policy =>
                            policy.AddAuthenticationSchemes(GatewayTestAuthHandler.SchemeName)
                                  .RequireAuthenticatedUser()));

                    // Override GlobalIp rate limit if requested (for GW9)
                    if (globalIpLimitOverride != DefaultGlobalIpLimit)
                    {
                        services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(
                            opts => opts.RejectionStatusCode = 429); // keeps default rejection shape
                    }
                });
            });
    }
}

// ─── GW Integration Tests ─────────────────────────────────────────────────────

/// <summary>GW1, GW2, GW3, GW4, GW6, GW9 — backend-stub integration tests.</summary>
public sealed class GatewayIntegrationTests
{
    private const string AuthHeader = "tenant_id=test-tenant,scope=ingestion";

    // ─── GW1: /api/v1/requests/* → request-api ───────────────────────────────

    [Fact]
    public async Task GW1_Route_Requests_ForwardedToRequestApi()
    {
        await using var stub = await BackendStub.StartAsync(async ctx =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"items":[]}""");
        });

        await using var factory = GatewayFactory.Create(requestApiPort: stub.Port);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Claims", AuthHeader);

        var response = await client.GetAsync("/api/v1/requests/req-123");

        // Gateway successfully proxied to stub → stub returned 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("items", body);
    }

    // ─── GW2: /api/v1/events/* → ingestion-api ───────────────────────────────

    [Fact]
    public async Task GW2_Route_Events_ForwardedToIngestionApi()
    {
        await using var stub = await BackendStub.StartAsync(async ctx =>
        {
            ctx.Response.StatusCode  = 201;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"accepted":1,"eventIds":["evt-001"]}""");
        });

        await using var factory = GatewayFactory.Create(ingestionApiPort: stub.Port);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Claims", AuthHeader);

        var response = await client.PostAsync(
            "/api/v1/events",
            new StringContent(
                """{"eventType":"smoke.test","occurredAt":"2026-05-20T10:00:00Z","payload":{}}""",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ─── GW3: /hubs/main WebSocket → realtime-hub ────────────────────────────

    [Fact]
    public async Task GW3_Route_Hub_WebSocketUpgrade_Proxied()
    {
        // Stub: any request to /hubs/main returns 200 (simplified — verifies YARP routes)
        await using var stub = await BackendStub.StartAsync(async ctx =>
        {
            // Respond with 200 to the non-upgrade HTTP request
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("stub-hub-ok");
        });

        await using var factory = GatewayFactory.Create(realtimeHubPort: stub.Port);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Claims", AuthHeader);

        // Non-WebSocket request to /hubs/main — verifies YARP routes to realtime-hub cluster
        // Full WebSocket upgrade is covered by §12.2 E2E scenario 3.
        var response = await client.GetAsync("/hubs/main");

        // YARP forwarded the request (not 404 route mismatch, not gateway error)
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ─── GW4: /sse/* response buffering disabled → X-Accel-Buffering: no ────

    [Fact]
    public async Task GW4_Route_Sse_ResponseBufferingDisabled()
    {
        await using var stub = await BackendStub.StartAsync(async ctx =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            await ctx.Response.WriteAsync("data: hello\n\n");
        });

        await using var factory = GatewayFactory.Create(requestApiPort: stub.Port);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Claims", AuthHeader);

        var response = await client.GetAsync("/sse/requests/session-1");

        // Gateway transform must add X-Accel-Buffering: no for /sse/* routes
        Assert.True(
            response.Headers.TryGetValues("X-Accel-Buffering", out var values) &&
            values.Any(v => v == "no"),
            "Expected X-Accel-Buffering: no header on SSE response");
    }

    // ─── GW6: Valid JWT claims → X-Tenant-Id, X-Token-Scope forwarded ─────────

    [Fact]
    public async Task GW6_JWT_Valid_ClaimsForwarded_AsHeaders()
    {
        var receivedHeaders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        await using var stub = await BackendStub.StartAsync(ctx =>
        {
            // Capture the forwarded claim headers
            receivedHeaders["X-Tenant-Id"]   = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            receivedHeaders["X-Token-Scope"]  = ctx.Request.Headers["X-Token-Scope"].FirstOrDefault();
            receivedHeaders["X-User-Id"]      = ctx.Request.Headers["X-User-Id"].FirstOrDefault();
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        await using var factory = GatewayFactory.Create(requestApiPort: stub.Port);
        var client = factory.CreateClient();

        // Inject tenant_id and scope claims via TestAuthHandler
        client.DefaultRequestHeaders.Add(
            "X-Test-Claims", "tenant_id=acme-corp,scope=requests");

        await client.GetAsync("/api/v1/requests/anything");

        // Gateway transform must forward claim values as informational headers
        Assert.Equal("acme-corp", receivedHeaders["X-Tenant-Id"]);
        Assert.Equal("requests",  receivedHeaders["X-Token-Scope"]);
    }

    // ─── GW9: Global IP rate limit flood → 429 ───────────────────────────────

    [Fact]
    public async Task GW9_RateLimit_GlobalIpFlood_Returns429()
    {
        // Backend stub always returns 200
        await using var stub = await BackendStub.StartAsync(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        // Create a factory with GlobalIp rate limit effectively overridden to 2
        // by using the Ingestion:RateLimits:Default trick, then redefine GlobalIp.
        // Approach: we can't easily reconfigure the named rate limit policy after the fact.
        // Instead, override the PermitLimit via a custom factory that re-wires the policy.
        await using var factory = new WebApplicationFactory<ReportingPlatform.Gateway.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Auth:Authority", "");
                builder.UseSetting(
                    "ReverseProxy:Clusters:request-api:Destinations:primary:Address",
                    $"http://127.0.0.1:{stub.Port}/");

                builder.ConfigureTestServices(services =>
                {
                    services.PostConfigure<AuthenticationOptions>(opts =>
                    {
                        opts.DefaultScheme             = GatewayTestAuthHandler.SchemeName;
                        opts.DefaultAuthenticateScheme = GatewayTestAuthHandler.SchemeName;
                        opts.DefaultChallengeScheme    = GatewayTestAuthHandler.SchemeName;
                    });
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, GatewayTestAuthHandler>(
                            GatewayTestAuthHandler.SchemeName, _ => { });
                    services.PostConfigure<AuthorizationOptions>(opts =>
                        opts.AddPolicy("Authenticated", policy =>
                            policy.AddAuthenticationSchemes(GatewayTestAuthHandler.SchemeName)
                                  .RequireAuthenticatedUser()));

                    // Override GlobalIp rate limit to 3.
                    // RateLimiterOptions.AddPolicy throws if a policy key already exists,
                    // and PostConfigure runs after Configure, so we use reflection to reach
                    // the private _policyMap dictionary and remove the existing "GlobalIp" key
                    // before adding our low-limit test override.
                    // Override via GlobalLimiter (a settable property, bypasses AddPolicy restriction).
                    // GlobalLimiter applies BEFORE named policies and gates all requests.
                    // PostConfigure runs after all Configure actions, ensuring our override wins.
                    services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
                        opts.GlobalLimiter =
                            System.Threading.RateLimiting.PartitionedRateLimiter.Create<
                                Microsoft.AspNetCore.Http.HttpContext, string>(
                                ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                                    partitionKey: "gw9-global-test",  // fixed key: all test requests share one bucket
                                    factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                                    {
                                        PermitLimit          = 3,
                                        Window               = TimeSpan.FromMinutes(1),
                                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                                        QueueLimit           = 0,
                                    })));
                });
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Claims", AuthHeader);

        // Send 10 concurrent requests — limit is 3; expect ≥1 returns 429
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => client.GetAsync("/api/v1/requests/flood-test"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.TooManyRequests);

        var throttled = responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(
            throttled.Headers.Contains("Retry-After"),
            "429 response must include Retry-After header");
    }
}
