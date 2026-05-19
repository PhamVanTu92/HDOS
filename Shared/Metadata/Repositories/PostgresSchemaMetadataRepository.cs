using Npgsql;

namespace ReportingPlatform.Metadata.Repositories;

public sealed class PostgresSchemaMetadataRepository : ISchemaMetadataRepository
{
    private static readonly JsonSerializerOptions SerOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PostgresSchemaMetadataRepository> _logger;

    public PostgresSchemaMetadataRepository(
        NpgsqlDataSource db,
        ILogger<PostgresSchemaMetadataRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<UpsertResult> UpsertAsync(
        string tenantId, SchemaDefinition definition, CancellationToken ct = default)
    {
        var bodyJson = definition.Schema.GetRawText();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schema_definitions (tenant_id, schema_id, schema_type, version, schema_body)
            VALUES ($1, $2, $3, $4, $5::jsonb)
            ON CONFLICT (tenant_id, schema_id)
            DO UPDATE SET
                schema_type = EXCLUDED.schema_type,
                version     = EXCLUDED.version,
                schema_body = EXCLUDED.schema_body,
                updated_at  = now()
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(definition.SchemaId);
        cmd.Parameters.AddWithValue(definition.SchemaType);
        cmd.Parameters.AddWithValue(definition.Version);
        cmd.Parameters.AddWithValue(bodyJson);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetInt64(0);

        return new UpsertResult { Id = id, Version = 1 };
    }

    public async Task<SchemaDefinition?> GetAsync(
        string tenantId, string schemaId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT schema_id, schema_type, version, schema_body
            FROM schema_definitions
            WHERE tenant_id = $1 AND schema_id = $2;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(schemaId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return MapRow(reader);
    }

    public async Task<IReadOnlyList<SchemaDefinition>> ListAsync(
        string tenantId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT schema_id, schema_type, version, schema_body
            FROM schema_definitions
            WHERE tenant_id = $1
            ORDER BY schema_id;
            """;
        cmd.Parameters.AddWithValue(tenantId);

        var results = new List<SchemaDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapRow(reader));

        return results;
    }

    private static SchemaDefinition MapRow(NpgsqlDataReader reader)
    {
        var bodyJson = reader.GetString(3);
        var schema   = JsonDocument.Parse(bodyJson).RootElement;
        return new SchemaDefinition
        {
            SchemaId   = reader.GetString(0),
            SchemaType = reader.GetString(1),
            Version    = reader.GetString(2),
            Schema     = schema,
        };
    }
}
