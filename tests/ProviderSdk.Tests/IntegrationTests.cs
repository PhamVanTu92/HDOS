extern alias SdkAlias;

namespace ReportingPlatform.ProviderSdk.Tests;

/// <summary>SI1 — End-to-end integration test. Requires Docker (Testcontainers). Enabled in Phase 12.</summary>
public sealed class IntegrationTests
{
    [Fact(Skip = "Requires Docker (Testcontainers) — enable in Phase 12")]
    public Task SI1_EndToEnd_ProviderServesRequest_ResponseOnSignalR()
    {
        // Phase 12: spin up Postgres + RabbitMQ + Redis + Request.Api + Provider.Bridge via Testcontainers
        // Register provider → get credentials → start ProviderClient → submit request → assert SignalR event
        return Task.CompletedTask;
    }
}
