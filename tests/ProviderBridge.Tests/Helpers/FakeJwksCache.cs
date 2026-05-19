namespace ReportingPlatform.ProviderBridge.Tests.Helpers;

/// <summary>
/// In-memory JWKS cache pre-loaded with a test public key.
/// Skips HTTP fetch entirely.
/// </summary>
public sealed class FakeJwksCache
{
    private readonly Dictionary<string, RsaSecurityKey> _keys = new(StringComparer.Ordinal);

    public void AddKey(string kid, RSA publicKey)
    {
        _keys[kid] = new RsaSecurityKey(publicKey) { KeyId = kid };
    }

    public Task<RsaSecurityKey?> GetKeyAsync(string kid, CancellationToken ct = default)
    {
        _keys.TryGetValue(kid, out var key);
        return Task.FromResult(key);
    }
}
