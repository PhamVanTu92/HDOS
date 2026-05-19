using System.Net.Http;
using System.Net.Http.Json;

namespace ReportingPlatform.Bridge.Services;

internal sealed class JwksDoc
{
    [JsonPropertyName("keys")]
    public List<JwkKeyEntry>? Keys { get; set; }
}

internal sealed class JwkKeyEntry
{
    [JsonPropertyName("kid")] public string? Kid { get; set; }
    [JsonPropertyName("n")]   public string? N   { get; set; }
    [JsonPropertyName("e")]   public string? E   { get; set; }
}

[JsonSerializable(typeof(JwksDoc))]
[JsonSerializable(typeof(JwkKeyEntry))]
[JsonSourceGenerationOptions]
internal sealed partial class JwksCacheJsonContext : JsonSerializerContext { }

public sealed class JwksCache : IHostedService
{
    private readonly HttpClient  _http;
    private readonly string      _jwksUrl;
    private readonly ILogger<JwksCache> _logger;

    private readonly ReaderWriterLockSlim _lock = new();
    private Dictionary<string, RsaSecurityKey> _keys = [];

    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private Timer?         _refreshTimer;

    public JwksCache(HttpClient http, IConfiguration config, ILogger<JwksCache> logger)
    {
        _http    = http;
        _jwksUrl = config["Bridge:JwksUrl"] ?? "http://localhost:5100/.well-known/jwks.json";
        _logger  = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        _refreshTimer = new Timer(_ => _ = RefreshAsync(CancellationToken.None),
                                  null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task StopAsync(CancellationToken ct)
    {
        _refreshTimer?.Dispose();
        return Task.CompletedTask;
    }

    public async Task<RsaSecurityKey?> GetKeyAsync(string kid, CancellationToken ct = default)
    {
        _lock.EnterReadLock();
        try
        {
            if (_keys.TryGetValue(kid, out var key)) return key;
        }
        finally { _lock.ExitReadLock(); }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastRefresh > TimeSpan.FromSeconds(30))
            await RefreshAsync(ct);

        _lock.EnterReadLock();
        try { _keys.TryGetValue(kid, out var key2); return key2; }
        finally { _lock.ExitReadLock(); }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var doc = await _http.GetFromJsonAsync<JwksDoc>(_jwksUrl,
                JwksCacheJsonContext.Default.JwksDoc, ct);
            if (doc?.Keys is null) return;

            var newKeys = new Dictionary<string, RsaSecurityKey>(StringComparer.Ordinal);
            foreach (var entry in doc.Keys)
            {
                if (entry.Kid is null || entry.N is null || entry.E is null) continue;
                var rsa = System.Security.Cryptography.RSA.Create();
                rsa.ImportParameters(new System.Security.Cryptography.RSAParameters
                {
                    Modulus  = Base64UrlDecode(entry.N),
                    Exponent = Base64UrlDecode(entry.E),
                });
                newKeys[entry.Kid] = new RsaSecurityKey(rsa) { KeyId = entry.Kid };
            }

            _lock.EnterWriteLock();
            try
            {
                _keys        = newKeys;
                _lastRefresh = DateTimeOffset.UtcNow;
            }
            finally { _lock.ExitWriteLock(); }

            _logger.LogInformation("JWKS refreshed: {Count} keys loaded", newKeys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWKS refresh failed from {Url}", _jwksUrl);
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
