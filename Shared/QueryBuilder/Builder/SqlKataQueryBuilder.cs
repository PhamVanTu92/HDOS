namespace ReportingPlatform.QueryBuilder.Builder;

using ReportingPlatform.Contracts.TableParams;

public sealed class SqlKataQueryBuilder
{
    private readonly NpgsqlDataSource _db;
    private readonly IQueryableSourceRepository _sources;
    private readonly ILogger<SqlKataQueryBuilder> _logger;

    private static readonly JsonElement NullElement;

    static SqlKataQueryBuilder()
    {
        using var doc = JsonDocument.Parse("null");
        NullElement = doc.RootElement.Clone();
    }

    public SqlKataQueryBuilder(
        NpgsqlDataSource db,
        IQueryableSourceRepository sources,
        ILogger<SqlKataQueryBuilder> logger)
    {
        _db      = db;
        _sources = sources;
        _logger  = logger;
    }

    /// <summary>
    /// Executes a paginated, filtered, sorted query against the whitelisted source.
    /// Throws <see cref="AdapterException"/> for any whitelist violation.
    /// </summary>
    public async Task<QueryBuilderResult> ExecuteAsync(
        string tenantId,
        string sourceName,
        TablePaginationParams pagination,
        CancellationToken ct = default)
    {
        // Whitelist check 1: source must exist and be active for this tenant
        var src = await _sources.GetAsync(tenantId, sourceName, ct)
            ?? throw new AdapterException("SOURCE_NOT_FOUND", sourceName);

        await using var conn = await _db.OpenConnectionAsync(ct);
        var factory = new QueryFactory(conn, new PostgresCompiler());

        // SqlKata's PostgresCompiler wraps "schema.table" → "schema"."table" correctly.
        var tableName = $"{src.SchemaName}.{src.TableName}";

        // Count query — apply filters only (no ORDER BY / LIMIT / OFFSET)
        var countQ = new Query(tableName);
        TableParamsApplicator.ApplyFiltersOnly(countQ, pagination, src);
        var totalRows = await factory.FromQuery(countQ)
            .CountAsync<long>(cancellationToken: ct);

        // Data query — full pagination (sorts + limit + offset + filters)
        var dataQ = BuildSelectQuery(src, tableName);
        TableParamsApplicator.Apply(dataQ, pagination, src);
        var rawRows = await factory.FromQuery(dataQ)
            .GetAsync<dynamic>(cancellationToken: ct);

        var rows = rawRows
            .Select(row =>
            {
                var dict = (IDictionary<string, object?>)row;
                return (IReadOnlyDictionary<string, JsonElement>)
                    dict.ToDictionary(
                        kv => kv.Key,
                        kv => ObjectToJsonElement(kv.Value),
                        StringComparer.Ordinal);
            })
            .ToList();

        _logger.LogDebug(
            "QueryBuilder: tenant={TenantId} source={SourceName} page={Page}/{TotalPages} rows={RowCount}",
            tenantId, sourceName, pagination.Page, totalRows, rows.Count);

        return new QueryBuilderResult(rows, totalRows);
    }

    private static Query BuildSelectQuery(QueryableSource src, string tableName)
    {
        var q = new Query(tableName);
        if (src.AllowedColumns.Count > 0)
            q.Select([.. src.AllowedColumns]);
        // else: SELECT * — SqlKata's default when no columns are specified
        return q;
    }

    // --- JsonElement conversion from Npgsql/Dapper column values ---

    private static JsonElement ObjectToJsonElement(object? value)
    {
        if (value is null or DBNull) return NullElement;

        return value switch
        {
            bool b             => JsonSerializer.SerializeToElement(b),
            int n              => JsonSerializer.SerializeToElement(n),
            long n             => JsonSerializer.SerializeToElement(n),
            short n            => JsonSerializer.SerializeToElement((int)n),
            byte n             => JsonSerializer.SerializeToElement((int)n),
            float f            => JsonSerializer.SerializeToElement(f),
            double d           => JsonSerializer.SerializeToElement(d),
            decimal m          => JsonSerializer.SerializeToElement(m),
            string s           => JsonSerializer.SerializeToElement(s),
            DateTime dt        => JsonSerializer.SerializeToElement(dt),
            DateTimeOffset dto => JsonSerializer.SerializeToElement(dto),
            DateOnly d         => JsonSerializer.SerializeToElement(d),
            TimeOnly t         => JsonSerializer.SerializeToElement(t),
            Guid g             => JsonSerializer.SerializeToElement(g),
            JsonElement el     => el,
            // Unknown types (e.g. NpgsqlRange, custom composites): safe ToString fallback
            _ => JsonSerializer.SerializeToElement(value.ToString() ?? string.Empty)
        };
    }
}
