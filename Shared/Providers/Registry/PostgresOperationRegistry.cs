using ReportingPlatform.Providers.Matching;

namespace ReportingPlatform.Providers.Registry;

internal sealed class PostgresOperationRegistry : IOperationRegistry
{
    private static readonly Meter _meter = new("ReportingPlatform.Providers");
    private static readonly Histogram<double> _reloadDuration =
        _meter.CreateHistogram<double>("operation_registry_reload_duration_seconds");

    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PostgresOperationRegistry> _logger;

    private RegistrySnapshot _snapshot = RegistrySnapshot.Empty;

    public PostgresOperationRegistry(NpgsqlDataSource db, ILogger<PostgresOperationRegistry> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default)
    {
        var snap = Volatile.Read(ref _snapshot);
        return Task.FromResult(WildcardMatcher.Resolve(operation, snap.All));
    }

    public Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Volatile.Read(ref _snapshot).All);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var rows = await LoadFromPostgresAsync(ct);
            var newSnap = BuildSnapshot(rows);
            Volatile.Write(ref _snapshot, newSnap);
            _reloadDuration.Record(sw.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("status", "success"));
            _logger.LogInformation("Operation registry reloaded: {Count} active registrations", newSnap.All.Count);
        }
        catch (Exception ex)
        {
            _reloadDuration.Record(sw.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("status", "error"));
            _logger.LogError(ex, "Operation registry reload failed");
            throw;
        }
    }

    private async Task<List<OperationRow>> LoadFromPostgresAsync(CancellationToken ct)
    {
        var rows = new List<OperationRow>();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT operation_pattern, handler_type, provider_id, schema_version,
                   params_schema, timeout_ms, cacheable, cache_ttl_seconds,
                   idempotent, required_role, status, deprecation_message
            FROM operation_registry
            WHERE status IN ('active', 'deprecated')
            ORDER BY operation_pattern
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new OperationRow(
                OperationPattern:   reader.GetString(0),
                HandlerType:        reader.GetString(1),
                ProviderId:         reader.IsDBNull(2)  ? null : reader.GetString(2),
                SchemaVersion:      reader.GetString(3),
                ParamsSchemaJson:   reader.IsDBNull(4)  ? null : reader.GetString(4),
                TimeoutMs:          reader.GetInt32(5),
                Cacheable:          reader.GetBoolean(6),
                CacheTtlSeconds:    reader.IsDBNull(7)  ? null : reader.GetInt32(7),
                Idempotent:         reader.GetBoolean(8),
                RequiredRole:       reader.IsDBNull(9)  ? null : reader.GetString(9),
                Status:             reader.GetString(10),
                DeprecationMessage: reader.IsDBNull(11) ? null : reader.GetString(11)
            ));
        }

        return rows;
    }

    private RegistrySnapshot BuildSnapshot(List<OperationRow> rows)
    {
        var registrations = new List<OperationRegistration>(rows.Count);

        foreach (var row in rows)
        {
            JsonElement? paramsSchema = null;
            JsonSchema? compiled = null;

            if (row.ParamsSchemaJson is not null)
            {
                try
                {
                    paramsSchema = JsonDocument.Parse(row.ParamsSchemaJson).RootElement;
                    var schemaDoc = JsonDocument.Parse(row.ParamsSchemaJson);

                    if (schemaDoc.RootElement.TryGetProperty("$schema", out var schemaProp))
                    {
                        var declared = schemaProp.GetString();
                        if (declared != "https://json-schema.org/draft/2020-12/schema")
                        {
                            _logger.LogWarning(
                                "Operation '{Pattern}' params_schema declares $schema '{Declared}' which is not Draft 2020-12; validation will still run but behaviour may differ.",
                                row.OperationPattern, declared);
                        }
                    }

                    compiled = JsonSchema.FromText(row.ParamsSchemaJson);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Operation '{Pattern}' has invalid params_schema; skipping schema compilation. Registration remains active without validation.",
                        row.OperationPattern);
                }
            }

            registrations.Add(new OperationRegistration
            {
                OperationPattern   = row.OperationPattern,
                HandlerType        = row.HandlerType,
                ProviderId         = row.ProviderId,
                SchemaVersion      = row.SchemaVersion,
                ParamsSchema       = paramsSchema,
                TimeoutMs          = row.TimeoutMs,
                Cacheable          = row.Cacheable,
                CacheTtlSeconds    = row.CacheTtlSeconds,
                Idempotent         = row.Idempotent,
                RequiredRole       = row.RequiredRole,
                Status             = row.Status,
                DeprecationMessage = row.DeprecationMessage,
                CompiledSchema     = compiled,
            });
        }

        var dict = registrations.ToDictionary(r => r.OperationPattern, StringComparer.Ordinal);
        return new RegistrySnapshot(dict, registrations);
    }

    private sealed record OperationRow(
        string  OperationPattern,
        string  HandlerType,
        string? ProviderId,
        string  SchemaVersion,
        string? ParamsSchemaJson,
        int     TimeoutMs,
        bool    Cacheable,
        int?    CacheTtlSeconds,
        bool    Idempotent,
        string? RequiredRole,
        string  Status,
        string? DeprecationMessage);

}
