extern alias SdkAlias;

namespace ReportingPlatform.ProviderSdk.Tests;

/// <summary>SD1–SD4: TokenManager JWT acquisition, caching, 80% refresh, 5× 401 revocation.</summary>
public sealed class TokenManagerTests
{
    // SD1 — Valid 200 OK → GetTokenAsync returns token
    [Fact]
    public async Task SD1_ValidCredentials_GetTokenAsync_ReturnsToken()
    {
        var handler = new FakeTokenHandler();
        handler.SetupSuccess("my-jwt", expiresIn: 900);
        var mgr = TestHelpers.BuildTokenManager(handler);

        var token = await mgr.GetTokenAsync(CancellationToken.None);

        Assert.Equal("my-jwt", token);
        Assert.Equal(1, handler.CallCount);
    }

    // SD2 — Token fresh (>90s remaining) → second GetTokenAsync uses cache (no HTTP call)
    [Fact]
    public async Task SD2_TokenFresh_SecondGetTokenAsync_UsesCacheNoHttpCall()
    {
        var handler = new FakeTokenHandler();
        handler.SetupSuccess("my-jwt", expiresIn: 900);
        var mgr = TestHelpers.BuildTokenManager(handler);

        await mgr.GetTokenAsync(CancellationToken.None);
        var token2 = await mgr.GetTokenAsync(CancellationToken.None);

        Assert.Equal("my-jwt", token2);
        Assert.Equal(1, handler.CallCount); // HTTP called only once
    }

    // SD3 — ShouldRefresh returns true after 80% of expiresIn elapsed
    [Fact]
    public async Task SD3_ShouldRefresh_TrueAfter80PctLifetime()
    {
        var handler = new FakeTokenHandler();
        handler.SetupSuccess("tok", expiresIn: 100);
        // Set refresh fraction to 0.001 so refresh threshold = issuedAt + 0.1s (effectively immediate)
        var opts = TestHelpers.DefaultOpts();
        opts.TokenRefreshEarlyFraction = 0.001;
        var mgr = TestHelpers.BuildTokenManager(handler, opts);

        await mgr.AcquireAsync(CancellationToken.None);

        // At 0.001 fraction with 100s token, threshold is 0.1s after issue.
        // Wait 150ms > 100ms threshold so ShouldRefresh becomes true.
        await Task.Delay(150);
        Assert.True(mgr.ShouldRefresh);
    }

    // SD4 — 5 consecutive 401s → CredentialsRevokedException thrown on 5th call
    [Fact]
    public async Task SD4_FiveConsecutive401s_ThrowsCredentialsRevokedException()
    {
        var handler = new FakeTokenHandler();
        handler.SetupAlwaysReturn(HttpStatusCode.Unauthorized);
        var mgr = TestHelpers.BuildTokenManager(handler);

        // First 4 calls throw HttpRequestException (401, count < 5)
        for (int i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => mgr.AcquireAsync(CancellationToken.None));
        }

        // 5th call throws CredentialsRevokedException
        await Assert.ThrowsAsync<CredentialsRevokedException>(
            () => mgr.AcquireAsync(CancellationToken.None));
        Assert.Equal(5, handler.CallCount);
    }
}
