using Microsoft.Extensions.Configuration;
using ReportingPlatform.ProviderBridge.Tests.Helpers;

namespace ReportingPlatform.ProviderBridge.Tests;

/// <summary>PE1–PE2: Probe endpoint logic tests.</summary>
public sealed class ProbeEndpointTests
{
    // ── PE1 — JwtIssuerService.IssueProbeToken has purpose=probe, exp ≤ 60s ──

    [Fact]
    public void PE1_ProbeToken_HasPurposeClaimAndShortExpiry()
    {
        var fakeKeys = new FakeSigningKeyService();
        var config   = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:ProviderIssuer"] = "https://test.platform/"
            })
            .Build();
        var issuer = new JwtIssuerService(fakeKeys, config);
        var jwt    = issuer.IssueProbeToken("probe-provider");

        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(jwt);

        var purpose = parsed.Claims.FirstOrDefault(c => c.Type == "purpose")?.Value;
        Assert.Equal("probe", purpose);

        var exp = parsed.ValidTo;
        var ttl = exp - DateTime.UtcNow;
        Assert.True(ttl.TotalSeconds <= 61, $"Probe token TTL should be ≤ 60s, got {ttl.TotalSeconds:F0}s");
    }

    // ── PE2 — JwtIssuerService.IssueProviderToken has no purpose claim ────────

    [Fact]
    public void PE2_RealToken_HasNoPurposeClaim()
    {
        var fakeKeys = new FakeSigningKeyService();
        var config   = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:ProviderIssuer"] = "https://test.platform/"
            })
            .Build();
        var issuer = new JwtIssuerService(fakeKeys, config);
        var jwt    = issuer.IssueProviderToken("real-provider");

        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(jwt);

        var purpose = parsed.Claims.FirstOrDefault(c => c.Type == "purpose");
        Assert.Null(purpose);

        // TTL should be ~900s.
        var ttl = parsed.ValidTo - DateTime.UtcNow;
        Assert.True(ttl.TotalSeconds > 890, $"Token TTL should be ~900s, got {ttl.TotalSeconds:F0}s");
    }
}
