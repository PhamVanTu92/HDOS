using Npgsql;
using ReportingPlatform.Contracts.RenderPayloads.Operations;

namespace ReportingPlatform.Metadata.Repositories;

public sealed class PostgresDashboardMetadataRepository : IDashboardMetadataRepository
{
    private static readonly JsonSerializerOptions SerOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly NpgsqlDataSource _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PostgresDashboardMetadataRepository> _logger;

    public PostgresDashboardMetadataRepository(
        NpgsqlDataSource db,
        IConnectionMultiplexer redis,
        ILogger<PostgresDashboardMetadataRepository> logger)
    {
        _db     = db;
        _redis  = redis;
        _logger = logger;
    }

    public async Task<UpsertResult> UpsertAsync(
        string tenantId, DashboardDefinition definition, CancellationToken ct = default)
    {
        var defJson = JsonSerializer.Serialize(definition, SerOpts);

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dashboard_definitions (tenant_id, dashboard_code, version, definition)
            VALUES ($1, $2, 1, $3::jsonb)
            ON CONFLICT (tenant_id, dashboard_code)
            DO UPDATE SET
                definition = EXCLUDED.definition,
                version    = dashboard_definitions.version + 1,
                updated_at = now()
            RETURNING id, version;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(definition.DashboardCode);
        cmd.Parameters.AddWithValue(defJson);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id      = reader.GetInt64(0);
        var version = reader.GetInt32(1);

        // Non-fatal cache invalidation pub/sub
        await PublishInvalidationAsync(definition.DashboardCode, tenantId);

        return new UpsertResult { Id = id, Version = version };
    }

    public async Task<DeleteResult> DeleteAsync(
        string tenantId, string dashboardCode, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM dashboard_definitions
            WHERE tenant_id = $1 AND dashboard_code = $2
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(dashboardCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var deleted = await reader.ReadAsync(ct);
        return new DeleteResult { Deleted = deleted };
    }

    public async Task<DashboardDefinition?> GetAsync(
        string tenantId, string dashboardCode, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT definition FROM dashboard_definitions
            WHERE tenant_id = $1 AND dashboard_code = $2;
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(dashboardCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var json = reader.GetString(0);
        return JsonSerializer.Deserialize<DashboardDefinition>(json, SerOpts);
    }

    public async Task<IReadOnlyList<DashboardMetadataSummary>> ListAsync(
        string tenantId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT dashboard_code,
                   definition->>'title'       AS title,
                   definition->>'description' AS description,
                   version
            FROM dashboard_definitions
            WHERE tenant_id = $1
            ORDER BY dashboard_code;
            """;
        cmd.Parameters.AddWithValue(tenantId);

        var results = new List<DashboardMetadataSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new DashboardMetadataSummary
            {
                DashboardCode = reader.GetString(0),
                Title         = reader.GetString(1),
                Description   = reader.IsDBNull(2) ? null : reader.GetString(2),
                Version       = reader.GetInt32(3),
            });
        }
        return results;
    }

    private async Task PublishInvalidationAsync(string dashboardCode, string tenantId)
    {
        try
        {
            var sub = _redis.GetSubscriber();
            await sub.PublishAsync(
                RedisChannel.Literal($"cache-invalidate:dashboard:{dashboardCode}"),
                tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Redis pub/sub publish failed for dashboard invalidation code={Code}", dashboardCode);
        }
    }
}
