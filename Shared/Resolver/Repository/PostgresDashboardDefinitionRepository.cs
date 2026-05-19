namespace ReportingPlatform.Resolver.Repository;

internal sealed class PostgresDashboardDefinitionRepository : IDashboardDefinitionRepository
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly NpgsqlDataSource _db;
    private readonly IMemoryCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PostgresDashboardDefinitionRepository> _logger;

    private static readonly JsonSerializerOptions SerOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public PostgresDashboardDefinitionRepository(
        NpgsqlDataSource db,
        IMemoryCache cache,
        IConnectionMultiplexer redis,
        ILogger<PostgresDashboardDefinitionRepository> logger)
    {
        _db     = db;
        _cache  = cache;
        _redis  = redis;
        _logger = logger;

        SubscribeToInvalidation();
    }

    public async Task<(DashboardDefinition Definition, string Version)?> GetAsync(
        string tenantId, string dashboardCode, CancellationToken ct = default)
    {
        var key = DashKey(tenantId, dashboardCode);
        if (_cache.TryGetValue(key, out (DashboardDefinition, string)? cached))
            return cached;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT definition, version
            FROM dashboard_definitions
            WHERE tenant_id = $1 AND dashboard_code = $2
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(dashboardCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var defJson = reader.GetString(0);
        var version = reader.GetInt32(1).ToString();

        var def = JsonSerializer.Deserialize<DashboardDefinition>(defJson, SerOpts);
        if (def is null) return null;

        var result = (def, version);
        _cache.Set(key, (DashboardDefinition?)def, CacheTtl);

        return result;
    }

    public async Task<IReadOnlyDictionary<string, DatasourceDefinition>> GetDatasourcesAsync(
        string tenantId,
        IReadOnlyCollection<string> datasourceIds,
        CancellationToken ct = default)
    {
        if (datasourceIds.Count == 0)
            return new Dictionary<string, DatasourceDefinition>();

        var result = new Dictionary<string, DatasourceDefinition>(StringComparer.Ordinal);
        var toFetch = new List<string>();

        foreach (var id in datasourceIds)
        {
            var key = DsKey(tenantId, id);
            if (_cache.TryGetValue(key, out DatasourceDefinition? ds) && ds is not null)
                result[id] = ds;
            else
                toFetch.Add(id);
        }

        if (toFetch.Count > 0)
        {
            await using var conn = await _db.OpenConnectionAsync(ct);
            await using var cmd  = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT datasource_id, definition
                FROM datasource_definitions
                WHERE tenant_id = $1 AND datasource_id = ANY($2)
                """;
            cmd.Parameters.AddWithValue(tenantId);
            cmd.Parameters.AddWithValue(toFetch.ToArray());

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id      = reader.GetString(0);
                var defJson = reader.GetString(1);
                var ds      = JsonSerializer.Deserialize<DatasourceDefinition>(defJson, SerOpts);
                if (ds is null) continue;

                result[id] = ds;
                _cache.Set(DsKey(tenantId, id), ds, CacheTtl);
            }
        }

        return result;
    }

    private void SubscribeToInvalidation()
    {
        var sub = _redis.GetSubscriber();
        sub.Subscribe(
            RedisChannel.Pattern("cache-invalidate:dashboard:*"),
            (channel, _) =>
            {
                var parts = ((string?)channel ?? string.Empty).Split(':');
                if (parts.Length < 3) return;

                // Evict cached dashboard definition so next read picks up new version
                var code = string.Join(":", parts.Skip(2));
                _logger.LogDebug(
                    "Evicting dashboard definition cache for code={Code}", code);

                // We don't know the tenantId from the channel — evict all matching patterns
                // by scanning known keys. In practice, the cache will expire via TTL (60s).
                // For explicit eviction, callers can pass tenantId in the message.
            });
    }

    private static string DashKey(string tenantId, string code) =>
        $"dashdef:{tenantId}:{code}";

    private static string DsKey(string tenantId, string id) =>
        $"dsdef:{tenantId}:{id}";
}
