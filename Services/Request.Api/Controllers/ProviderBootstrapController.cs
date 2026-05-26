using Npgsql;
using ReportingPlatform.RequestApi.Services;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// POST /api/v1/providers/bootstrap
/// Allows a provider to fetch its own ClientSecret on startup using a bootstrap token.
/// This eliminates the need to hardcode secrets in .env files.
///
/// Flow:
///   1. Admin sets/rotates secret via Credentials UI → HDOS stores hash + encrypted plaintext + bootstrap_token in DB
///   2. Provider config: contains HDOS_BOOTSTRAP_URL + HDOS_BOOTSTRAP_TOKEN (not the real secret)
///   3. On startup, provider calls this endpoint → receives its ClientSecret
///   4. Provider uses ClientSecret to authenticate via /api/v1/providers/token
/// </summary>
[ApiController]
[Route("api/v1/providers")]
[AllowAnonymous]
public sealed class ProviderBootstrapController : ControllerBase
{
    private readonly NpgsqlDataSource      _db;
    private readonly ProviderSecretService _secretSvc;
    private readonly ILogger<ProviderBootstrapController> _logger;

    public ProviderBootstrapController(
        NpgsqlDataSource db,
        ProviderSecretService secretSvc,
        ILogger<ProviderBootstrapController> logger)
    {
        _db        = db;
        _secretSvc = secretSvc;
        _logger    = logger;
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> BootstrapAsync(
        [FromBody] ProviderBootstrapRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClientId) || string.IsNullOrWhiteSpace(req.BootstrapToken))
            return BadRequest(new { error = "clientId and bootstrapToken are required." });

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT client_secret_enc
            FROM provider_registry
            WHERE client_id        = $1
              AND bootstrap_token  = $2
              AND status           = 'active'
              AND client_secret_enc IS NOT NULL
            """;
        cmd.Parameters.AddWithValue(req.ClientId.Trim());
        cmd.Parameters.AddWithValue(req.BootstrapToken.Trim());

        var enc = await cmd.ExecuteScalarAsync(ct) as string;
        if (enc is null)
        {
            _logger.LogWarning("Bootstrap failed for clientId={ClientId} — token mismatch or secret not set", req.ClientId);
            return Unauthorized(new { error = "invalid_bootstrap" });
        }

        try
        {
            var plaintext = _secretSvc.Decrypt(enc);
            _logger.LogInformation("Bootstrap secret issued for clientId={ClientId}", req.ClientId);
            return Ok(new { clientSecret = plaintext });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret for clientId={ClientId}", req.ClientId);
            return StatusCode(500, new { error = "decryption_failed" });
        }
    }
}

public sealed record ProviderBootstrapRequest
{
    public required string ClientId      { get; init; }
    public required string BootstrapToken { get; init; }
}
