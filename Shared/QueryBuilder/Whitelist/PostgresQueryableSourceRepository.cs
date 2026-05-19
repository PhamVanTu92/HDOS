namespace ReportingPlatform.QueryBuilder.Whitelist;

internal sealed class PostgresQueryableSourceRepository : IQueryableSourceRepository
{
    // Cache key: "qs:{tenantId}:{sourceName}"
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly NpgsqlDataSource _db;
    private readonly IMemoryCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PostgresQueryableSourceRepository> _logger;

    public PostgresQueryableSourceRepository(
        NpgsqlDataSource db,
        IMemoryCache cache,
        IConnectionMultiplexer redis,
        ILogger<PostgresQueryableSourceRepository> logger)
    {
        _db     = db;
        _cache  = cache;
        _redis  = redis;
        _logger = logger;

        SubscribeToInvalidation();
    }

    public async Task<QueryableSource?> GetAsync(string tenantId, string sourceName, CancellationToken ct = default)
    {
        var key = CacheKey(tenantId, sourceName);
        if (_cache.TryGetValue(key, out QueryableSource? cached))
            return cached;

        var row = await LoadFromPostgresAsync(tenantId, sourceName, ct);
        if (row is not null)
            _cache.Set(key, row, CacheTtl);

        return row;
    }

    private async Task<QueryableSource?> LoadFromPostgresAsync(
        string tenantId, string sourceName, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT schema_name, table_name, allowed_columns, sortable_columns, max_rows
            FROM queryable_sources
            WHERE tenant_id = $1 AND source_name = $2 AND status = 'active'
            """;
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(sourceName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new QueryableSource
        {
            TenantId        = tenantId,
            SourceName      = sourceName,
            SchemaName      = reader.GetString(0),
            TableName       = reader.GetString(1),
            AllowedColumns  = ParseStringArray(reader.GetString(2)),
            SortableColumns = ParseStringArray(reader.GetString(3)),
            MaxRows         = reader.GetInt32(4),
        };
    }

    private void SubscribeToInvalidation()
    {
        var sub = _redis.GetSubscriber();
        sub.Subscribe(
            RedisChannel.Pattern("cache-invalidate:queryable-sources:*"),
            (_, msg) =>
            {
                var parts = ((string?)msg)?.Split(':');
                if (parts is null) return;

                // Evict all cache entries for this tenantId
                var tenantId = string.Join(":", parts.Skip(1));
                _logger.LogDebug("Queryable sources cache invalidated for tenant {TenantId}", tenantId);

                // MemoryCache doesn't support prefix eviction; we rely on TTL expiry.
                // For correctness, individual entries are also evicted via specific source keys
                // published by the admin endpoint: "cache-invalidate:queryable-sources:{tenantId}:{sourceName}"
                if (parts.Length >= 2)
                {
                    var key = CacheKey(parts[0], string.Join(":", parts.Skip(1)));
                    _cache.Remove(key);
                }
            });
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return [];
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static string CacheKey(string tenantId, string sourceName) =>
        $"qs:{tenantId}:{sourceName}";
}
