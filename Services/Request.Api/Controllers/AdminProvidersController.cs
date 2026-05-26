using Grpc.Core;
using Grpc.Net.Client;
using Npgsql;
using ReportingPlatform.Auth;
using ReportingPlatform.Provider.V1;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// Admin endpoints for provider management, credential rotation, and gRPC connectivity probe.
/// Requires role: admin (enforced via [Authorize(Roles = "admin")]).
/// </summary>
[ApiController]
[Route("api/v1/admin/providers")]
[Authorize(Roles = "admin")]
public sealed class AdminProvidersController : ControllerBase
{
    private readonly IProviderRegistry    _registry;
    private readonly JwtIssuerService     _issuer;
    private readonly NpgsqlDataSource     _db;
    private readonly IConfiguration       _config;
    private readonly ProviderSecretService _secretSvc;
    private readonly ILogger<AdminProvidersController> _logger;

    public AdminProvidersController(
        IProviderRegistry registry,
        JwtIssuerService issuer,
        NpgsqlDataSource db,
        IConfiguration config,
        ProviderSecretService secretSvc,
        ILogger<AdminProvidersController> logger)
    {
        _registry  = registry;
        _issuer    = issuer;
        _db        = db;
        _config    = config;
        _secretSvc = secretSvc;
        _logger    = logger;
    }

    // ── GET /api/v1/admin/providers ──────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var rows = new List<object>();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT provider_id, display_name, description, client_id,
                   operations, timeout_ms, priority, status,
                   created_at, updated_at
            FROM provider_registry
            ORDER BY display_name
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new
            {
                providerId   = reader.GetString(0),
                displayName  = reader.GetString(1),
                description  = reader.IsDBNull(2) ? null : reader.GetString(2),
                clientId     = reader.GetString(3),
                operations   = (string[])(reader.GetValue(4) ?? Array.Empty<string>()),
                timeoutMs    = reader.GetInt32(5),
                priority     = reader.GetInt16(6),
                status       = reader.GetString(7),
                createdAt    = reader.GetDateTime(8),
                updatedAt    = reader.GetDateTime(9),
            });
        }

        return Ok(rows);
    }

    // ── POST /api/v1/admin/providers ─────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterProviderRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ProviderId) ||
            string.IsNullOrWhiteSpace(req.ClientId)   ||
            string.IsNullOrWhiteSpace(req.ClientSecret))
            return BadRequest(new { error = "providerId, clientId and clientSecret are required." });

        var secretHash = BCrypt.Net.BCrypt.HashPassword(req.ClientSecret, workFactor: 12);
        var ops        = req.Operations ?? [];

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx   = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var insert = conn.CreateCommand();
            insert.Transaction  = tx;
            insert.CommandText  = """
                INSERT INTO provider_registry
                    (provider_id, display_name, description, client_id,
                     client_secret_hash, operations, timeout_ms, priority, status)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'active')
                ON CONFLICT (provider_id) DO NOTHING
                RETURNING provider_id
                """;
            insert.Parameters.AddWithValue(req.ProviderId.Trim());
            insert.Parameters.AddWithValue(req.DisplayName?.Trim() ?? req.ProviderId.Trim());
            insert.Parameters.AddWithValue(req.Description is null ? DBNull.Value : (object)req.Description.Trim());
            insert.Parameters.AddWithValue(req.ClientId.Trim());
            insert.Parameters.AddWithValue(secretHash);
            insert.Parameters.AddWithValue(ops);
            insert.Parameters.AddWithValue(req.TimeoutMs > 0 ? req.TimeoutMs : 30_000);
            insert.Parameters.AddWithValue((short)Math.Clamp(req.Priority, 1, 10));

            var created = await insert.ExecuteScalarAsync(ct);
            if (created is null)
            {
                await tx.RollbackAsync(ct);
                return Conflict(new { error = $"Provider '{req.ProviderId}' already exists." });
            }

            await using var audit = conn.CreateCommand();
            audit.Transaction  = tx;
            audit.CommandText  = """
                INSERT INTO provider_credentials_audit (provider_id, action, performed_by)
                VALUES ($1, 'register', $2)
                """;
            audit.Parameters.AddWithValue(req.ProviderId.Trim());
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

        return Created($"/api/v1/admin/providers/{req.ProviderId}",
            new { providerId = req.ProviderId, registeredAt = DateTimeOffset.UtcNow });
    }

    // ── PUT /api/v1/admin/providers/{id} ────────────────────────────────────

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(
        string id,
        [FromBody] UpdateProviderRequest req,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE provider_registry SET
                display_name = COALESCE($2, display_name),
                description  = COALESCE($3, description),
                operations   = COALESCE($4, operations),
                timeout_ms   = COALESCE($5, timeout_ms),
                priority     = COALESCE($6, priority),
                status       = COALESCE($7, status),
                updated_at   = NOW()
            WHERE provider_id = $1
            RETURNING provider_id, display_name, description, client_id,
                      operations, timeout_ms, priority, status,
                      created_at, updated_at
            """;
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(req.DisplayName is null ? DBNull.Value : (object)req.DisplayName.Trim());
        cmd.Parameters.AddWithValue(req.Description is null ? DBNull.Value : (object)req.Description.Trim());
        cmd.Parameters.AddWithValue(req.Operations is null ? DBNull.Value : (object)req.Operations);
        cmd.Parameters.AddWithValue(req.TimeoutMs is null ? DBNull.Value : (object)req.TimeoutMs.Value);
        cmd.Parameters.AddWithValue(req.Priority is null ? DBNull.Value : (object)(short)Math.Clamp(req.Priority.Value, 1, 10));
        cmd.Parameters.AddWithValue(req.Status is null ? DBNull.Value : (object)req.Status.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return NotFound(new { error = $"Provider '{id}' not found." });

        var result = new
        {
            providerId  = reader.GetString(0),
            displayName = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            clientId    = reader.GetString(3),
            operations  = (string[])(reader.GetValue(4) ?? Array.Empty<string>()),
            timeoutMs   = reader.GetInt32(5),
            priority    = reader.GetInt16(6),
            status      = reader.GetString(7),
            createdAt   = reader.GetDateTime(8),
            updatedAt   = reader.GetDateTime(9),
        };
        await reader.CloseAsync();

        await _registry.ReloadAsync(ct);

        return Ok(result);
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

        // Also store encrypted plaintext so admin can retrieve / bootstrap later
        await StoreEncryptedSecretAsync(id, newSecret, conn, ct);

        return Ok(new { providerId = id, rotatedAt = DateTimeOffset.UtcNow.ToString("O"), newSecret });
    }

    // ── POST /api/v1/admin/providers/{id}/credentials/set ───────────────────
    /// <summary>Set a specific plaintext secret (admin-provided). Hash + encrypt + store.</summary>

    [HttpPost("{id}/credentials/set")]
    public async Task<IActionResult> SetSecretAsync(
        string id,
        [FromBody] SetSecretRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.NewSecret))
            return BadRequest(new { error = "newSecret is required." });

        var provider = await _registry.GetAsync(id, ct);
        if (provider is null) return NotFound(new { error = $"Provider '{id}' not found." });

        var newHash = BCrypt.Net.BCrypt.HashPassword(req.NewSecret.Trim(), workFactor: 12);
        var newEnc  = _secretSvc.Encrypt(req.NewSecret.Trim());

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx   = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE provider_registry SET
                    client_secret_hash = $1,
                    client_secret_enc  = $2,
                    updated_at         = NOW()
                WHERE provider_id = $3
                """;
            update.Parameters.AddWithValue(newHash);
            update.Parameters.AddWithValue(newEnc);
            update.Parameters.AddWithValue(id);
            await update.ExecuteNonQueryAsync(ct);

            await using var audit = conn.CreateCommand();
            audit.Transaction = tx;
            audit.CommandText = """
                INSERT INTO provider_credentials_audit (provider_id, action, performed_by)
                VALUES ($1, 'set_secret', $2)
                """;
            audit.Parameters.AddWithValue(id);
            audit.Parameters.AddWithValue(AdminUserId());
            await audit.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }

        await _registry.ReloadAsync(ct);

        _logger.LogInformation("Secret set for provider {ProviderId} by admin {AdminId}", id, AdminUserId());
        return Ok(new { providerId = id, updatedAt = DateTimeOffset.UtcNow.ToString("O") });
    }

    // ── GET /api/v1/admin/providers/{id}/credentials/reveal ─────────────────
    /// <summary>Reveal the stored encrypted secret (admin only). Returns plaintext once.</summary>

    [HttpGet("{id}/credentials/reveal")]
    public async Task<IActionResult> RevealAsync(string id, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT client_secret_enc FROM provider_registry WHERE provider_id = $1";
        cmd.Parameters.AddWithValue(id);

        var enc = await cmd.ExecuteScalarAsync(ct) as string;
        if (enc is null)
            return NotFound(new { error = "No stored secret for this provider. Use 'Set Secret' first." });

        try
        {
            var plaintext = _secretSvc.Decrypt(enc);
            _logger.LogWarning("Secret revealed for provider {ProviderId} by admin {AdminId}", id, AdminUserId());
            return Ok(new { clientSecret = plaintext });
        }
        catch
        {
            return UnprocessableEntity(new { error = "Secret could not be decrypted — key may have changed. Re-set the secret." });
        }
    }

    // ── POST /api/v1/admin/providers/{id}/bootstrap-token/regenerate ────────
    /// <summary>Regenerate bootstrap token. Provider uses this to fetch its secret on startup.</summary>

    [HttpPost("{id}/bootstrap-token/regenerate")]
    public async Task<IActionResult> RegenerateBootstrapTokenAsync(string id, CancellationToken ct)
    {
        var provider = await _registry.GetAsync(id, ct);
        if (provider is null) return NotFound(new { error = $"Provider '{id}' not found." });

        var token = ProviderSecretService.GenerateBootstrapToken();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE provider_registry
            SET bootstrap_token = $1, updated_at = NOW()
            WHERE provider_id = $2
            """;
        cmd.Parameters.AddWithValue(token);
        cmd.Parameters.AddWithValue(id);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Bootstrap token regenerated for provider {ProviderId}", id);
        return Ok(new { providerId = id, bootstrapToken = token });
    }

    // ── GET /api/v1/admin/providers/{id}/bootstrap-token ────────────────────

    [HttpGet("{id}/bootstrap-token")]
    public async Task<IActionResult> GetBootstrapTokenAsync(string id, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT bootstrap_token FROM provider_registry WHERE provider_id = $1";
        cmd.Parameters.AddWithValue(id);

        var token = await cmd.ExecuteScalarAsync(ct) as string;
        return Ok(new { providerId = id, bootstrapToken = token });
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
            // Bridge throws Unauthenticated for both invalid JWT and "Provider not found"
            // (after successful JWT validation). Use the status detail to disambiguate.
            var detail = ex.Status.Detail ?? string.Empty;
            jwtAccepted  = detail.Contains("Provider not found", StringComparison.OrdinalIgnoreCase);
            errorDetail  = jwtAccepted ? "provider_not_in_bridge_registry" : "jwt_rejected";
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

    /// <summary>Encrypt and store plaintext secret in client_secret_enc column.</summary>
    private async Task StoreEncryptedSecretAsync(
        string providerId, string plaintext, Npgsql.NpgsqlConnection conn, CancellationToken ct)
    {
        try
        {
            var enc = _secretSvc.Encrypt(plaintext);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE provider_registry
                SET client_secret_enc = $1
                WHERE provider_id = $2
                """;
            cmd.Parameters.AddWithValue(enc);
            cmd.Parameters.AddWithValue(providerId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store encrypted secret for provider {ProviderId}", providerId);
        }
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record UpdateProviderRequest
{
    public string?   DisplayName { get; init; }
    public string?   Description { get; init; }
    public string[]? Operations  { get; init; }
    public int?      TimeoutMs   { get; init; }
    public int?      Priority    { get; init; }
    public string?   Status      { get; init; }
}

public sealed record SetSecretRequest
{
    public required string NewSecret { get; init; }
}

public sealed record RegisterProviderRequest
{
    public required string   ProviderId    { get; init; }
    public string?           DisplayName   { get; init; }
    public string?           Description   { get; init; }
    public required string   ClientId      { get; init; }
    public required string   ClientSecret  { get; init; }
    public string[]?         Operations    { get; init; }
    public int               TimeoutMs     { get; init; } = 30_000;
    public int               Priority      { get; init; } = 5;
}
