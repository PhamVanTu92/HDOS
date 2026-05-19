using System.Text;
using ReportingPlatform.Auth;

namespace ReportingPlatform.RequestApi.Controllers;

[ApiController]
public sealed class JwksController : ControllerBase
{
    private readonly ISigningKeyService _keys;

    public JwksController(ISigningKeyService keys) => _keys = keys;

    [HttpGet("/.well-known/jwks.json")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public IActionResult GetJwks()
    {
        var entries = new List<JwkEntry>();
        foreach (var (kid, spkiBytes) in _keys.JwksKeys)
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(spkiBytes, out _);
            var p = rsa.ExportParameters(false);
            entries.Add(new JwkEntry
            {
                Kty = "RSA",
                Use = "sig",
                Alg = "RS256",
                Kid = kid,
                N   = Base64UrlEncode(p.Modulus!),
                E   = Base64UrlEncode(p.Exponent!),
            });
        }
        var doc = new JwksDocument { Keys = entries };
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(doc);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
