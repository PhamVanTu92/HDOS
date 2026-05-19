using Microsoft.Extensions.Hosting;

namespace ReportingPlatform.Auth;

public sealed class SigningKeyService : ISigningKeyService, IHostedService
{
    private readonly NpgsqlDataSource _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<SigningKeyService> _logger;

    private volatile KeySnapshot _snapshot = KeySnapshot.Empty;

    private Timer? _refreshTimer;

    private const string DataProtectionPurpose = "Provider.Auth.SigningKeys";

    public SigningKeyService(
        NpgsqlDataSource db,
        IDataProtectionProvider dataProtection,
        ILogger<SigningKeyService> logger)
    {
        _db        = db;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
        _logger    = logger;
    }

    public RsaSecurityKey ActiveKey => _snapshot.ActiveKey;
    public string         ActiveKid => _snapshot.ActiveKid;
    public IReadOnlyList<(string Kid, byte[] PublicKeySpki)> JwksKeys => _snapshot.JwksKeys;

    public async Task StartAsync(CancellationToken ct)
    {
        await EnsureActiveKeyExistsAsync(ct);
        await ReloadAsync(ct);
        _refreshTimer = new Timer(_ => _ = ReloadAsync(CancellationToken.None),
                                  null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task StopAsync(CancellationToken ct)
    {
        _refreshTimer?.Dispose();
        return Task.CompletedTask;
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await LoadRowsAsync(ct);
            _snapshot = BuildSnapshot(rows);
            _logger.LogInformation("Signing keys reloaded: {Count} keys in JWKS", _snapshot.JwksKeys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload signing keys");
            throw;
        }
    }

    private async Task EnsureActiveKeyExistsAsync(CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM signing_keys WHERE status = 'active'";
        var count = (long)(await check.ExecuteScalarAsync(ct) ?? 0L);
        if (count > 0) return;

        _logger.LogInformation("No active signing key found — generating new RSA-2048 key");
        using var rsa          = RSA.Create(2048);
        var pkcs8Bytes         = rsa.ExportPkcs8PrivateKey();
        var spkiBytes          = rsa.ExportSubjectPublicKeyInfo();
        var encryptedPrivate   = _protector.Protect(pkcs8Bytes);
        var kid                = Guid.CreateVersion7().ToString();

        await using var insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO signing_keys (key_id, algorithm, private_key_encrypted, public_key_spki, status)
            VALUES ($1, 'RS256', $2, $3, 'active')
            """;
        insert.Parameters.AddWithValue(kid);
        insert.Parameters.AddWithValue(encryptedPrivate);
        insert.Parameters.AddWithValue(spkiBytes);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private async Task<List<KeyRow>> LoadRowsAsync(CancellationToken ct)
    {
        var rows = new List<KeyRow>();
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT key_id, private_key_encrypted, public_key_spki, status, retires_at
            FROM signing_keys
            WHERE status IN ('active', 'retired')
              AND (retires_at IS NULL OR retires_at > NOW() - INTERVAL '65 minutes')
            ORDER BY created_at DESC
            """;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new KeyRow(
                KeyId:               reader.GetString(0),
                PrivateKeyEncrypted: (byte[])reader.GetValue(1),
                PublicKeySpki:       (byte[])reader.GetValue(2),
                Status:              reader.GetString(3),
                RetiresAt:           reader.IsDBNull(4) ? (DateTimeOffset?)null
                                         : reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return rows;
    }

    private KeySnapshot BuildSnapshot(List<KeyRow> rows)
    {
        KeyRow? activeRow = null;
        foreach (var r in rows)
        {
            if (r.Status == "active") { activeRow = r; break; }
        }

        if (activeRow is null)
            throw new InvalidOperationException("No active signing key found in Postgres after reload.");

        var privateBytes = _protector.Unprotect(activeRow.PrivateKeyEncrypted);
        var rsa          = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateBytes, out _);
        var activeKey    = new RsaSecurityKey(rsa) { KeyId = activeRow.KeyId };

        var jwksKeys = rows
            .Select(r => (r.KeyId, r.PublicKeySpki))
            .ToList();

        return new KeySnapshot(activeKey, activeRow.KeyId, jwksKeys);
    }

    private sealed record KeyRow(
        string          KeyId,
        byte[]          PrivateKeyEncrypted,
        byte[]          PublicKeySpki,
        string          Status,
        DateTimeOffset? RetiresAt);

    private sealed record KeySnapshot(
        RsaSecurityKey                                    ActiveKey,
        string                                            ActiveKid,
        IReadOnlyList<(string Kid, byte[] PublicKeySpki)> JwksKeys)
    {
        public static readonly KeySnapshot Empty =
            new(new RsaSecurityKey(RSA.Create(2048)), string.Empty, []);
    }
}
