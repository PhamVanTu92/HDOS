using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;

namespace ReportingPlatform.Auth;

public sealed class JwtIssuerService
{
    private readonly ISigningKeyService _keys;
    private readonly string             _issuer;
    private const    string             Audience          = "provider-bridge";
    private const    int                TokenLifetimeSec  = 900;
    private const    int                ProbeLifetimeSec  = 60;

    public JwtIssuerService(ISigningKeyService keys, IConfiguration config)
    {
        _keys   = keys;
        _issuer = config["Auth:ProviderIssuer"] ?? "https://platform.reporting/";
    }

    public string IssueProviderToken(string providerId)
    {
        var jti   = Guid.CreateVersion7().ToString();
        var now   = DateTime.UtcNow;
        var creds = new SigningCredentials(_keys.ActiveKey, SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, providerId),
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Jti, jti),
            new System.Security.Claims.Claim("scope", "provider"),
            new System.Security.Claims.Claim("aud",   Audience),
        };
        var token = new JwtSecurityToken(
            issuer:             _issuer,
            claims:             claims,
            notBefore:          now,
            expires:            now.AddSeconds(TokenLifetimeSec),
            signingCredentials: creds);
        token.Header["kid"] = _keys.ActiveKid;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string IssueProbeToken(string providerId)
    {
        var jti   = Guid.CreateVersion7().ToString();
        var now   = DateTime.UtcNow;
        var creds = new SigningCredentials(_keys.ActiveKey, SecurityAlgorithms.RsaSha256);
        var claims = new[]
        {
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, providerId),
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Jti, jti),
            new System.Security.Claims.Claim("scope", "provider"),
            new System.Security.Claims.Claim("aud",   Audience),
            new System.Security.Claims.Claim("purpose", "probe"),
        };
        var token = new JwtSecurityToken(
            issuer:             _issuer,
            claims:             claims,
            notBefore:          now,
            expires:            now.AddSeconds(ProbeLifetimeSec),
            signingCredentials: creds);
        token.Header["kid"] = _keys.ActiveKid;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string ExtractJti(string jwt)
    {
        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(jwt);
        return parsed.Id;
    }
}
