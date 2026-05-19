namespace ReportingPlatform.ProviderBridge.Tests.Helpers;

/// <summary>
/// Creates real RSA-2048 keys and issues real JWTs for testing.
/// Separate from FakeSigningKeyService — used for integration-level JWT tests.
/// </summary>
public static class TestJwtFactory
{
    public const string DefaultIssuer   = "https://test.platform/";
    public const string DefaultAudience = "provider-bridge";
    public const string DefaultScope    = "provider";

    public static (RSA PrivateKey, RSA PublicKey, string Kid) GenerateKeyPair()
    {
        var rsa = RSA.Create(2048);
        var kid = Guid.CreateVersion7().ToString();
        // Export public key as separate RSA instance for validation tests.
        var pubRsa = RSA.Create();
        pubRsa.ImportSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo(), out _);
        return (rsa, pubRsa, kid);
    }

    public static string IssueToken(
        RSA        signingKey,
        string     kid,
        string     providerId,
        string     issuer   = DefaultIssuer,
        string     audience = DefaultAudience,
        string     scope    = DefaultScope,
        int        lifetimeSec = 900,
        string?    purpose  = null,
        bool       tamperSignature = false)
    {
        var secKey  = new RsaSecurityKey(signingKey) { KeyId = kid };
        var creds   = new SigningCredentials(secKey, SecurityAlgorithms.RsaSha256);
        var claims  = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, providerId),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new("scope", scope),
        };
        if (purpose is not null) claims.Add(new("purpose", purpose));

        var now     = DateTime.UtcNow;
        var expires = now.AddSeconds(lifetimeSec);
        // If lifetimeSec is negative, set notBefore far in the past so the ctor doesn't reject it.
        var notBefore = lifetimeSec < 0 ? expires.AddSeconds(-60) : now;

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          notBefore,
            expires:            expires,
            signingCredentials: creds);
        token.Header["kid"] = kid;

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        if (tamperSignature)
        {
            // Flip the last byte of the signature.
            var parts    = jwt.Split('.');
            var sigBytes = Base64UrlDecode(parts[2]);
            sigBytes[^1] ^= 0xFF;
            parts[2]      = Base64UrlEncode(sigBytes);
            return string.Join('.', parts);
        }
        return jwt;
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
