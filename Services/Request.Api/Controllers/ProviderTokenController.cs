using Npgsql;
using ReportingPlatform.Auth;
using ReportingPlatform.RequestApi.Services;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// POST /api/v1/providers/token — OAuth2 client_credentials grant for providers.
/// Security invariants:
/// - 401 body is IDENTICAL for unknown clientId and wrong clientSecret (no oracle attack).
/// - Lockout check runs BEFORE BCrypt verify.
/// - Audit log is fire-and-forget (does not delay the response).
/// </summary>
[ApiController]
[Route("api/v1/providers")]
public sealed class ProviderTokenController : ControllerBase
{
    private readonly IProviderRegistry       _registry;
    private readonly JwtIssuerService        _issuer;
    private readonly ProviderLockoutService  _lockout;
    private readonly NpgsqlDataSource        _db;
    private readonly ILogger<ProviderTokenController> _logger;

    private static readonly object InvalidClientBody = new { error = "invalid_client" };
    private static readonly object RateLimitedBody   = new { error = "rate_limited", retryAfterSeconds = 60 };

    public ProviderTokenController(
        IProviderRegistry registry,
        JwtIssuerService issuer,
        ProviderLockoutService lockout,
        NpgsqlDataSource db,
        ILogger<ProviderTokenController> logger)
    {
        _registry = registry;
        _issuer   = issuer;
        _lockout  = lockout;
        _db       = db;
        _logger   = logger;
    }

    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TokenAsync(
        [FromBody] ProviderTokenRequest request,
        CancellationToken ct)
    {
        if (!string.Equals(request.GrantType, "client_credentials", StringComparison.Ordinal))
            return BadRequest(new { error = "unsupported_grant_type" });

        var clientId = request.ClientId;

        if (await _lockout.IsRateLimitedAsync(clientId, ct))
            return StatusCode(StatusCodes.Status429TooManyRequests, RateLimitedBody);

        if (await _lockout.IsLockedOutAsync(clientId, ct))
            return Unauthorized(InvalidClientBody);

        var valid = await _registry.ValidateCredentialsAsync(clientId, request.ClientSecret, ct);
        if (!valid)
        {
            var lockedOut = await _lockout.RecordFailureAsync(clientId, ct);
            if (lockedOut)
                _logger.LogWarning("Provider {ClientId} locked out after repeated failures", clientId);
            return Unauthorized(InvalidClientBody);
        }

        var allActive = await _registry.GetAllActiveAsync(ct);
        ProviderRegistration? registration = null;
        foreach (var r in allActive)
        {
            if (string.Equals(r.ClientId, clientId, StringComparison.Ordinal))
            {
                registration = r;
                break;
            }
        }
        if (registration is null)
            return Unauthorized(InvalidClientBody);

        await _lockout.ClearFailuresAsync(clientId, ct);

        var jwt = _issuer.IssueProviderToken(registration.ProviderId);
        var jti = JwtIssuerService.ExtractJti(jwt);

        _ = WriteAuditAsync(registration.ProviderId, "issue", jti, clientId);

        return Ok(new
        {
            accessToken = jwt,
            expiresIn   = 900,
            tokenType   = "Bearer",
        });
    }

    private async Task WriteAuditAsync(string providerId, string action, string jti, string performedBy)
    {
        try
        {
            await using var conn = await _db.OpenConnectionAsync();
            await using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO provider_credentials_audit (provider_id, action, jti, performed_by)
                VALUES ($1, $2, $3, $4)
                """;
            cmd.Parameters.AddWithValue(providerId);
            cmd.Parameters.AddWithValue(action);
            cmd.Parameters.AddWithValue(jti);
            cmd.Parameters.AddWithValue($"system:{performedBy}");
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for {Action} by {PerformedBy}", action, performedBy);
        }
    }
}
