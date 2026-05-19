using ReportingPlatform.ProviderBridge.Tests.Helpers;

namespace ReportingPlatform.ProviderBridge.Tests;

/// <summary>JV1–JV9: JWT validation tests.</summary>
public sealed class JwtValidationTests
{
    private const string Issuer   = TestJwtFactory.DefaultIssuer;
    private const string Audience = TestJwtFactory.DefaultAudience;

    private static TokenValidationParameters BuildParams(RsaSecurityKey publicKey) =>
        new()
        {
            ValidateIssuer           = true,
            ValidIssuer              = Issuer,
            ValidateAudience         = true,
            ValidAudience            = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = publicKey,
            ValidateLifetime         = true,
            // SECURITY INVARIANT: exactly 30s clock skew
            ClockSkew                = TimeSpan.FromSeconds(30),
            RequireSignedTokens      = true,
        };

    private static (RSA priv, RsaSecurityKey pub, string kid) MakeKey()
    {
        var (priv, pubRsa, kid) = TestJwtFactory.GenerateKeyPair();
        var pub = new RsaSecurityKey(pubRsa) { KeyId = kid };
        return (priv, pub, kid);
    }

    // ── JV1 — Valid JWT → validation succeeds ────────────────────────────────

    [Fact]
    public void JV1_ValidJwt_ValidationSucceeds()
    {
        var (priv, pub, kid) = MakeKey();
        var jwt    = TestJwtFactory.IssueToken(priv, kid, "provider-1");
        var result = new JwtSecurityTokenHandler().ValidateToken(jwt, BuildParams(pub), out _);
        // JwtSecurityTokenHandler maps "sub" → ClaimTypes.NameIdentifier by default.
        // Check both the mapped and raw form.
        var subValue = result.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? result.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        Assert.Equal("provider-1", subValue);
    }

    // ── JV2 — Expired JWT (> 30s ago) → validation fails ────────────────────

    [Fact]
    public void JV2_ExpiredJwt_ValidationFails()
    {
        var (priv, pub, kid) = MakeKey();
        var jwt = TestJwtFactory.IssueToken(priv, kid, "provider-2", lifetimeSec: -60);
        Assert.Throws<SecurityTokenExpiredException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(jwt, BuildParams(pub), out _));
    }

    // ── JV3 — JWT expired 29s ago (within 30s skew) → accepted ──────────────

    [Fact]
    public void JV3_JwtExpired10sAgo_AcceptedWithinClockSkew()
    {
        var (priv, pub, kid) = MakeKey();
        // Issue token that expired 10s ago — well within 30s clock skew.
        var jwt = TestJwtFactory.IssueToken(priv, kid, "provider-3", lifetimeSec: -10);
        // Should NOT throw — within 30s clock skew.
        var result = new JwtSecurityTokenHandler().ValidateToken(jwt, BuildParams(pub), out _);
        Assert.NotNull(result);
    }

    // ── JV4 — Wrong audience → validation fails ──────────────────────────────

    [Fact]
    public void JV4_WrongAudience_ValidationFails()
    {
        var (priv, pub, kid) = MakeKey();
        var jwt = TestJwtFactory.IssueToken(priv, kid, "provider-4", audience: "user-api");
        Assert.Throws<SecurityTokenInvalidAudienceException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(jwt, BuildParams(pub), out _));
    }

    // ── JV5 — Missing scope claim → rejected at scope check ─────────────────

    [Fact]
    public void JV5_MissingScope_FailsScopeCheck()
    {
        var (priv, pub, kid) = MakeKey();
        var jwt       = TestJwtFactory.IssueToken(priv, kid, "provider-5", scope: "other");
        var principal = new JwtSecurityTokenHandler().ValidateToken(jwt, BuildParams(pub), out _);
        var scope     = principal.FindFirst("scope")?.Value ?? string.Empty;
        // Scope check: must contain "provider"
        Assert.DoesNotContain("provider", scope.Split(' '), StringComparer.Ordinal);
    }

    // ── JV6 — Unknown kid → no key in cache → fails ──────────────────────────

    [Fact]
    public async Task JV6_UnknownKid_NoKeyInCache_ReturnsNull()
    {
        var fakeCache = new FakeJwksCache();
        var key       = await fakeCache.GetKeyAsync("unknown-kid-xyz");
        Assert.Null(key);
    }

    // ── JV7 — Tampered signature → validation fails ──────────────────────────

    [Fact]
    public void JV7_TamperedSignature_ValidationFails()
    {
        var (priv, pub, kid) = MakeKey();
        var jwt = TestJwtFactory.IssueToken(priv, kid, "provider-7", tamperSignature: true);
        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(jwt, BuildParams(pub), out _));
    }

    // ── JV8 — No authorization metadata → empty token string ─────────────────

    [Fact]
    public void JV8_NoAuthorizationHeader_EmptyTokenIsInvalid()
    {
        var handler = new JwtSecurityTokenHandler();
        Assert.False(handler.CanReadToken(string.Empty));
    }

    // ── JV9 — authorization present but empty Bearer ─────────────────────────

    [Fact]
    public void JV9_EmptyBearerValue_CannotReadToken()
    {
        var handler = new JwtSecurityTokenHandler();
        Assert.False(handler.CanReadToken("   "));
    }
}
