using System.Net;
using System.Net.Http.Json;

namespace DotnetProviderSample.SelfRegistration;

public sealed class ProviderSelfRegistrar : IHostedService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ProviderSelfRegistrar> _logger;

    public ProviderSelfRegistrar(IHttpClientFactory factory, IConfiguration config, ILogger<ProviderSelfRegistrar> logger)
    {
        _http   = factory.CreateClient();
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var adminBase  = _config["Provider:AdminBase"] ?? "http://request-api:8080/api/v1/admin";
        var providerId = _config["Provider:ProviderId"] ?? "ml-team-fraud";

        try
        {
            var check = await _http.GetAsync($"{adminBase}/providers/{providerId}", ct);
            if (check.IsSuccessStatusCode)
            {
                _logger.LogInformation("Provider {Id} already registered", providerId);
                return;
            }
            if (check.StatusCode != HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Unexpected status {Code} checking provider — skipping self-registration", check.StatusCode);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reach admin API — skipping self-registration");
            return;
        }

        // Register
        var registration = new
        {
            providerId    = providerId,
            displayName   = "ML Team — Fraud Scoring (Sample)",
            description   = "Sample .NET provider: ml.fraud.score + ml.fraud.batchScore",
            operations    = new[] { "ml.fraud.score", "ml.fraud.batchScore" },
            timeoutMs     = 5000,
            maxConcurrentRequests = 8,
        };

        try
        {
            var resp = await _http.PostAsJsonAsync($"{adminBase}/providers", registration, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Self-registration failed: {Code}", resp.StatusCode);
                return;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Self-registration succeeded. SAVE clientSecret NOW (shown once): {Body}", body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Self-registration request failed");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
