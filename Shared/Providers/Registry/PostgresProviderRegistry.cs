using ReportingPlatform.Providers.Serialization;

namespace ReportingPlatform.Providers.Registry;

internal sealed class PostgresProviderRegistry : IProviderRegistry
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PostgresProviderRegistry> _logger;

    // Keyed by provider_id; also indexed by client_id for credential lookup.
    private ProviderSnapshot _snapshot = ProviderSnapshot.Empty;

    public PostgresProviderRegistry(NpgsqlDataSource db, ILogger<PostgresProviderRegistry> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<ProviderRegistration?> GetAsync(string providerId, CancellationToken ct = default)
    {
        var snap = Volatile.Read(ref _snapshot);
        snap.ById.TryGetValue(providerId, out var reg);
        return Task.FromResult(reg is { Status: "active" } ? reg : null);
    }

    public Task<IReadOnlyList<ProviderRegistration>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Volatile.Read(ref _snapshot).Active);
    }

    public Task<bool> ValidateCredentialsAsync(string clientId, string clientSecret, CancellationToken ct = default)
    {
        var snap = Volatile.Read(ref _snapshot);
        if (!snap.ByClientId.TryGetValue(clientId, out var reg))
            return Task.FromResult(false);

        // BCrypt work factor 12; ~250ms verify cost is intentional.
        var valid = BCrypt.Net.BCrypt.Verify(clientSecret, reg.ClientSecretHash);
        return Task.FromResult(valid);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await LoadFromPostgresAsync(ct);
            var newSnap = BuildSnapshot(rows);
            Volatile.Write(ref _snapshot, newSnap);
            _logger.LogInformation("Provider registry reloaded: {Count} providers", rows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider registry reload failed");
            throw;
        }
    }

    private async Task<List<ProviderRow>> LoadFromPostgresAsync(CancellationToken ct)
    {
        var rows = new List<ProviderRow>();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT provider_id, display_name, client_id, client_secret_hash,
                   operations, chart_types, transformers,
                   timeout_ms, circuit_breaker, priority, status
            FROM provider_registry
            ORDER BY provider_id
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ProviderRow(
                ProviderId:        reader.GetString(0),
                DisplayName:       reader.GetString(1),
                ClientId:          reader.GetString(2),
                ClientSecretHash:  reader.GetString(3),
                Operations:        reader.GetFieldValue<string[]>(4),
                ChartTypes:        reader.GetFieldValue<string[]>(5),
                Transformers:      reader.GetFieldValue<string[]>(6),
                TimeoutMs:         reader.GetInt32(7),
                CircuitBreakerJson:reader.GetString(8),
                Priority:          reader.GetInt16(9),
                Status:            reader.GetString(10)
            ));
        }

        return rows;
    }

    private static ProviderSnapshot BuildSnapshot(List<ProviderRow> rows)
    {
        var registrations = new List<ProviderRegistration>(rows.Count);

        foreach (var row in rows)
        {
            var cb = JsonSerializer.Deserialize(row.CircuitBreakerJson, ProvidersJsonContext.Default.CircuitBreakerConfig)
                  ?? new CircuitBreakerConfig();

            registrations.Add(new ProviderRegistration
            {
                ProviderId       = row.ProviderId,
                DisplayName      = row.DisplayName,
                ClientId         = row.ClientId,
                ClientSecretHash = row.ClientSecretHash,
                Operations       = row.Operations,
                ChartTypes       = row.ChartTypes,
                Transformers     = row.Transformers,
                TimeoutMs        = row.TimeoutMs,
                CircuitBreaker   = cb,
                Priority         = row.Priority,
                Status           = row.Status,
            });
        }

        var byId       = registrations.ToDictionary(r => r.ProviderId, StringComparer.Ordinal);
        var byClientId = registrations.ToDictionary(r => r.ClientId, StringComparer.Ordinal);
        var active     = (IReadOnlyList<ProviderRegistration>)registrations
            .Where(r => r.Status == "active")
            .ToList();

        return new ProviderSnapshot(byId, byClientId, active);
    }

    private sealed record ProviderRow(
        string   ProviderId,
        string   DisplayName,
        string   ClientId,
        string   ClientSecretHash,
        string[] Operations,
        string[] ChartTypes,
        string[] Transformers,
        int      TimeoutMs,
        string   CircuitBreakerJson,
        short    Priority,
        string   Status);

    private sealed record ProviderSnapshot(
        IReadOnlyDictionary<string, ProviderRegistration> ById,
        IReadOnlyDictionary<string, ProviderRegistration> ByClientId,
        IReadOnlyList<ProviderRegistration> Active)
    {
        public static readonly ProviderSnapshot Empty =
            new(new Dictionary<string, ProviderRegistration>(),
                new Dictionary<string, ProviderRegistration>(),
                []);
    }
}
