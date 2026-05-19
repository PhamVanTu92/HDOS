using Npgsql;
using ReportingPlatform.Contracts.RenderPayloads.Operations;

namespace ReportingPlatform.Metadata.Repositories;

public sealed class PostgresDatasourceMetadataRepository : IDatasourceMetadataRepository
{
    private static readonly JsonSerializerOptions SerOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PostgresDatasourceMetadataRepository> _logger;

    public PostgresDatasourceMetadataRepository(
        NpgsqlDataSource db,
        ILogger<PostgresDatasourceMetadataRepository> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<UpsertResult> UpsertAsync(
        string tenantId, DatasourceDefinition definition, CancellationToken ct = default)
    {
        var defJson = JsonSerializer.Serialize(definition, SerOpts);

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO datasource_definitions (tenant_id, datasource_id, definition)
            VALUES ($1, $2, $3::jsonb)
            ON CONFLICT (tenant_id, datasource_id)
            DO UPDATE SET
                definition = EXCLUDED.definition,
                updated_at = now()
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(definition.DatasourceId);
        cmd.Parameters.AddWithValue(defJson);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetInt64(0);

        // datasource_definitions has no version column; return version=1 as sentinel
        return new UpsertResult { Id = id, Version = 1 };
    }

    public async Task<DeleteResult> DeleteAsync(
        string tenantId, string datasourceId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM datasource_definitions
            WHERE tenant_id = $1 AND datasource_id = $2
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(datasourceId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var deleted = await reader.ReadAsync(ct);
        return new DeleteResult { Deleted = deleted };
    }

    public async Task<DatasourceDefinition?> GetAsync(
        string tenantId, string datasourceId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT definition FROM datasource_definitions
            WHERE tenant_id = $1 AND datasource_id = $2;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(datasourceId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var json = reader.GetString(0);
        return JsonSerializer.Deserialize<DatasourceDefinition>(json, SerOpts);
    }

    public async Task<IReadOnlyList<DatasourceSummary>> ListAsync(
        string tenantId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT definition->>'datasourceId' AS datasource_id,
                   definition->>'displayName'  AS display_name,
                   definition->>'description'  AS description,
                   definition->>'type'         AS type
            FROM datasource_definitions
            WHERE tenant_id = $1
            ORDER BY definition->>'datasourceId';
            """;
        cmd.Parameters.AddWithValue(tenantId);

        var results = new List<DatasourceSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new DatasourceSummary
            {
                DatasourceId = reader.GetString(0),
                DisplayName  = reader.GetString(1),
                Description  = reader.IsDBNull(2) ? null : reader.GetString(2),
                Type         = reader.GetString(3),
            });
        }
        return results;
    }
}
