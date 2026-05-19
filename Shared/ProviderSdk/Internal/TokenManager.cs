namespace ReportingPlatform.ProviderSdk.Internal;

internal sealed class TokenManager
{
    private readonly HttpClient _http;
    private readonly ProviderSdkOptions _opts;
    private readonly ILogger _logger;

    private string?        _cachedToken;
    private DateTimeOffset _issuedAt;
    private int            _expiresIn;  // seconds
    private int            _consecutive401s;

    // Sync root for cached token access
    private readonly SemaphoreSlim _lock = new(1, 1);

    // ActivitySource for telemetry
    private static readonly ActivitySource _actSource = ProviderSdkActivitySource.Source;

    public TokenManager(HttpClient http, ProviderSdkOptions opts, ILogger logger)
    {
        _http   = http;
        _opts   = opts;
        _logger = logger;
    }

    /// <summary>Returns cached token if still fresh (>90s remaining), else fetches a new one.</summary>
    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _issuedAt.AddSeconds(_expiresIn - 90))
                return _cachedToken;
            return await FetchAndCacheAsync(ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Force-acquire a fresh token (called by ConnectionManager on RefreshAuth / reconnect).</summary>
    public async Task<string> AcquireAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { return await FetchAndCacheAsync(ct); }
        finally { _lock.Release(); }
    }

    /// <summary>True when the cached token has passed the 80% lifetime threshold.</summary>
    public bool ShouldRefresh =>
        _cachedToken is not null &&
        DateTimeOffset.UtcNow > _issuedAt.AddSeconds(_expiresIn * _opts.TokenRefreshEarlyFraction);

    private async Task<string> FetchAndCacheAsync(CancellationToken ct)
    {
        // Must be called while _lock is held.
        using var activity = _actSource.StartActivity("sdk.token.acquire");

        var requestBody = new Models.TokenRequest
        {
            ClientId     = _opts.ClientId,
            ClientSecret = _opts.ClientSecret,
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(
                _opts.TokenEndpoint, requestBody,
                SdkJsonContext.Default.TokenRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token endpoint network error");
            throw;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ProviderSdkConfigurationException(
                $"Token endpoint returned 400 Bad Request — fix SDK configuration. Body: {body}");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _consecutive401s++;
            _logger.LogWarning("Token endpoint 401 (consecutive={Count})", _consecutive401s);
            if (_consecutive401s >= 5)
            {
                _logger.LogCritical("5 consecutive 401s — credentials revoked or permanently invalid");
                throw new CredentialsRevokedException();
            }
            throw new HttpRequestException($"401 Unauthorized from token endpoint (attempt {_consecutive401s})");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfterSec = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60.0;
            _logger.LogWarning("Token endpoint 429 — RetryAfter={Seconds}s", retryAfterSec);
            // Rethrow — ConnectionManager will observe this in the backoff loop
            throw new HttpRequestException($"429 Too Many Requests — retry after {retryAfterSec}s")
            {
                Data = { ["retryAfterSeconds"] = retryAfterSec }
            };
        }

        response.EnsureSuccessStatusCode();
        _consecutive401s = 0; // reset on success

        var tokenResponse = await response.Content.ReadFromJsonAsync(
            SdkJsonContext.Default.TokenResponse, ct)
            ?? throw new InvalidOperationException("Token endpoint returned null response body.");

        _cachedToken = tokenResponse.AccessToken;
        _issuedAt    = DateTimeOffset.UtcNow;
        _expiresIn   = tokenResponse.ExpiresIn;

        _logger.LogDebug("Acquired JWT — expiresIn={ExpiresIn}s refreshAt={RefreshAt:O}",
            _expiresIn,
            _issuedAt.AddSeconds(_expiresIn * _opts.TokenRefreshEarlyFraction));

        return _cachedToken;
    }
}
