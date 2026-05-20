using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ReportingPlatform.Gateway.Tests.Gateway;

/// <summary>
/// GW1–GW10 — YARP Gateway integration tests.
///
/// Uses WebApplicationFactory to spin up the Gateway service in-process.
/// YARP routes are configured from appsettings.json; backends are replaced with
/// stub test servers where needed. Tests that require actual proxy forwarding
/// (GW1–GW4) use a test infrastructure that captures proxied requests.
/// </summary>
public sealed class GatewayTests : IClassFixture<WebApplicationFactory<ReportingPlatform.Gateway.Program>>
{
    private readonly WebApplicationFactory<ReportingPlatform.Gateway.Program> _factory;

    public GatewayTests(WebApplicationFactory<ReportingPlatform.Gateway.Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override YARP cluster destinations to point at stub test servers
                // embedded in the test. Phase 12 will wire this up fully.
                // For now, JWT, CORS, rate-limiting, and health checks are testable
                // without actual backend forwarding.
            });

            builder.UseSetting("Auth:Authority", "https://test.authority.local");
            builder.UseSetting("Auth:Audience",  "test-audience");
        });
    }

    // ─── GW1–GW4: Backend-stub tests ─────────────────────────────────────────
    // Implemented in Integration/GatewayIntegrationTests.cs (Phase 12)

    // ─── GW5: Invalid JWT → 401 (gateway rejects; backend never called) ──────

    [Fact]
    public async Task GW5_JWT_Invalid_Returns401_BeforeBackend()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // No Authorization header — should be rejected by gateway JWT middleware.
        var response = await client.GetAsync("/api/v1/requests/result/test-id");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── GW6: Backend-stub test — see Integration/GatewayIntegrationTests.cs

    // ─── GW7: CORS preflight — allowed origin → 200 with CORS headers ────────

    [Fact]
    public async Task GW7_CORS_Preflight_AllowedOrigin_Returns200_WithHeaders()
    {
        var client = _factory.WithWebHostBuilder(b =>
        {
            // Inject a test origin into the allowed list.
            b.UseSetting("Cors:AllowedOrigins:0", "http://localhost:3000");
        }).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/events");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization, Content-Type");

        var response = await client.SendAsync(request);

        // CORS preflight should return 204 or 200.
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.NoContent ||
            response.StatusCode == System.Net.HttpStatusCode.OK,
            $"Expected 204/200 but got {(int)response.StatusCode}");

        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "CORS response header Access-Control-Allow-Origin missing");
    }

    // ─── GW8: CORS preflight — disallowed origin → no CORS header ─────────────

    [Fact]
    public async Task GW8_CORS_Preflight_DisallowedOrigin_NoCorsHeaders()
    {
        var client = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Cors:AllowedOrigins:0", "http://localhost:3000");
        }).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/events");
        request.Headers.Add("Origin", "https://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "CORS Allow-Origin header must not be present for disallowed origin");
    }

    // ─── GW9: Rate-limit flood test — see Integration/GatewayIntegrationTests.cs

    // ─── GW10: /health → 200 without authentication ──────────────────────────

    [Fact]
    public async Task GW10_Health_Returns200_WithoutAuth()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
