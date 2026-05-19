using Grpc.Core;
using Grpc.Net.Client;
using Npgsql;
using ReportingPlatform.Auth;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// Admin endpoints for provider credential management and connectivity probe.
/// Requires role: admin (enforced via [Authorize(Roles = "admin")]).
/// </summary>
[ApiController]
[Route("api/v1/admin/providers")]
[Authorize(Roles = "admin")]
public sealed class AdminProvidersController : ControllerBase
{
    private readonly IProviderRegistry _registry;
    private readonly JwtIssuerService  _issuer;
    private readonly NpgsqlDataSource  _db;
    private readonly IConfiguration    _config;
    private readonly ILogger<AdminProvidersController> _logger;

    public AdminProvidersController(
        IProviderRegistry registry,
        JwtIssuerService issuer,
        NpgsqlDataSource db,
        IConfiguration config,
        ILogger<AdminProvidersController> logger)
    {
        _registry = registry;
        _issuer   = issuer;
        _db       = db;
        _config   = config;
        _logger   = logger;
    }

    // ── POST /api/v1/admin/providers/{id}/credentials/rotate ─────────────────

    [HttpPost("{id}/credentials/rotate")]
    public async Task<IActionResult> RotateAsync(string id, CancellationToken ct)
    {
        var provider = await _registry.GetAsync(id, ct);
        if (provider is null) return NotFound();

        var newSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var newHash   = BCrypt.Net.BCrypt.HashPassword(newSecret, workFactor: 12);

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx   = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE provider_registry SET
                    pending_client_secret_hash = client_secret_hash,
                    pending_secret_expires_at  = NOW() + INTERVAL '60 seconds',
                    client_secret_hash         = $1,
                    updated_at                 = NOW()
                WHERE provider_id = $2
                """;
            update.Parameters.AddWithValue(newHash);
            update.Parameters.AddWithValue(id);
            await update.ExecuteNonQueryAsync(ct);

            await using var audit = conn.CreateCommand();
            audit.Transaction = tx;
            audit.CommandText = """
                INSERT INTO provider_credentials_audit (provider_id, action, performed_by)
                VALUES ($1, 'rotate', $2)
                """;
            audit.Parameters.AddWithValue(id);
            audit.Parameters.AddWithValue(AdminUserId());
            await audit.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await _registry.ReloadAsync(ct);

        return Ok(new { providerId = id, rotatedAt = DateTimeOffset.UtcNow.ToString("O") });
    }

    // ── POST /api/v1/admin/providers/{id}/credentials/revoke ─────────────────

    [HttpPost("{id}/credentials/revoke")]
    public async Task<IActionResult> RevokeAsync(string id, CancellationToken ct)
    {
        var provider = await _registry.GetAsync(id, ct);
        if (provider is null) return NotFound();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx   = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE provider_registry SET status = 'credentials_revoked', updated_at = NOW()
                WHERE provider_id = $1
                """;
            update.Parameters.AddWithValue(id);
            await update.ExecuteNonQueryAsync(ct);

            await using var audit = conn.CreateCommand();
            audit.Transaction = tx;
            audit.CommandText = """
                INSERT INTO provider_credentials_audit (provider_id, action, performed_by)
                VALUES ($1, 'revoke', $2)
                """;
            audit.Parameters.AddWithValue(id);
            audit.Parameters.AddWithValue(AdminUserId());
            await audit.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await _registry.ReloadAsync(ct);
        return Accepted();
    }

    // ── POST /api/v1/admin/signing-keys/rotate ──────────────────────────────

    [HttpPost("/api/v1/admin/signing-keys/rotate")]
    public async Task<IActionResult> RotateSigningKeyAsync(
        [FromServices] SigningKeyService signingKeys,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        using var rsa          = RSA.Create(2048);
        var pkcs8Bytes         = rsa.ExportPkcs8PrivateKey();
        var spkiBytes          = rsa.ExportSubjectPublicKeyInfo();

        var protector = HttpContext.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Provider.Auth.SigningKeys");
        var encryptedPrivate = protector.Protect(pkcs8Bytes);
        var newKid           = Guid.CreateVersion7().ToString();

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO signing_keys (key_id, algorithm, private_key_encrypted, public_key_spki, status)
                VALUES ($1, 'RS256', $2, $3, 'active')
                """;
            insert.Parameters.AddWithValue(newKid);
            insert.Parameters.AddWithValue(encryptedPrivate);
            insert.Parameters.AddWithValue(spkiBytes);
            await insert.ExecuteNonQueryAsync(ct);

            await using var retire = conn.CreateCommand();
            retire.Transaction = tx;
            retire.CommandText = """
                UPDATE signing_keys SET status = 'retired', retires_at = NOW() + INTERVAL '60 minutes'
                WHERE status = 'active' AND key_id != $1
                """;
            retire.Parameters.AddWithValue(newKid);
            await retire.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await signingKeys.ReloadAsync(ct);
        return Ok(new { kid = newKid, rotatedAt = DateTimeOffset.UtcNow.ToString("O") });
    }

    // ── POST /api/v1/admin/providers/{id}/probe ──────────────────────────────

    [HttpPost("{id}/probe")]
    public async Task<IActionResult> ProbeAsync(string id, CancellationToken ct)
    {
        var provider = await _registry.GetAsync(id, ct);
        if (provider is null) return NotFound();

        var bridgeUrl       = _config["Bridge:GrpcUrl"] ?? "http://localhost:5400";
        var probeJwt        = _issuer.IssueProbeToken(id);
        var probeJti        = JwtIssuerService.ExtractJti(probeJwt);
        var sw              = System.Diagnostics.Stopwatch.StartNew();
        bool tlsHandshake   = false, jwtAccepted = false, welcomeReceived = false;
        string? errorDetail = null;
        string? sessionId   = null;

        try
        {
            using var channel = GrpcChannel.ForAddress(bridgeUrl);
            var client = new OperationProvider.OperationProviderClient(channel);
            var headers = new Grpc.Core.Metadata
            {
                { "authorization", $"Bearer {probeJwt}" }
            };
            using var stream = client.Connect(headers, cancellationToken: ct);
            tlsHandshake = true;
            jwtAccepted  = true;

            await stream.RequestStream.WriteAsync(new FromProvider
            {
                Hello = new Hello { ProviderId = id }
            }, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await foreach (var msg in stream.ResponseStream.ReadAllAsync(timeoutCts.Token))
            {
                if (msg.MessageCase == ToProvider.MessageOneofCase.Welcome)
                {
                    welcomeReceived = true;
                    sessionId       = msg.Welcome.SessionId;
                    break;
                }
                if (msg.MessageCase == ToProvider.MessageOneofCase.Disconnect) break;
            }

            await stream.RequestStream.CompleteAsync();
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unauthenticated)
        {
            tlsHandshake = true;
            jwtAccepted  = false;
            errorDetail  = "jwt_rejected";
        }
        catch (OperationCanceledException)
        {
            errorDetail = "timeout";
        }
        catch (Exception ex)
        {
            errorDetail = ex.Message;
        }

        _ = WriteProbeAuditAsync(id, probeJti, probeJwt);

        return Ok(new
        {
            tlsHandshake,
            jwtAccepted,
            welcomeReceived,
            latencyMs   = (int)sw.ElapsedMilliseconds,
            sessionId,
            errorDetail,
        });
    }

    private async Task WriteProbeAuditAsync(string providerId, string jti, string performedBy)
    {
        try
        {
            await using var conn = await _db.OpenConnectionAsync();
            await using var cmd  = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO provider_credentials_audit (provider_id, action, jti, performed_by)
                VALUES ($1, 'probe', $2, $3)
                """;
            cmd.Parameters.AddWithValue(providerId);
            cmd.Parameters.AddWithValue(jti);
            cmd.Parameters.AddWithValue(AdminUserId());
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write probe audit for provider {ProviderId}", providerId);
        }
    }

    private string AdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-admin";
}
