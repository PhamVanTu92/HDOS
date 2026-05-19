namespace ReportingPlatform.Adapters.Implementations;

/// <summary>
/// Adapter for datasources with mode="raw".
/// Executes an admin-defined parameterised SQL template.
/// Filter values are bound as named Dapper parameters (@paramName).
/// </summary>
// Not sealed — TimescaleAdapter inherits shared row-conversion helpers.
internal class SqlRawAdapter : IDatasourceAdapter
{
    protected readonly NpgsqlDataSource Db;
    private readonly ILogger<SqlRawAdapter> _logger;

    public SqlRawAdapter(NpgsqlDataSource db, ILogger<SqlRawAdapter> logger)
    {
        Db      = db;
        _logger = logger;
    }

    public virtual async Task<AdapterResult> FetchAsync(
        AdapterRequest request, CancellationToken ct = default)
    {
        var config = ParseConfig(request.Datasource);

        if (string.IsNullOrWhiteSpace(config.Template))
            throw new AdapterException(
                "MISSING_TEMPLATE",
                $"datasource '{request.Datasource.DatasourceId}' has no 'template' in ConnectionConfig");

        _logger.LogDebug(
            "SqlRawAdapter: tenant={TenantId} datasource={DatasourceId}",
            request.TenantId, request.Datasource.DatasourceId);

        await using var conn = await Db.OpenConnectionAsync(ct);

        var parameters = BuildParameters(request.Filters);
        var rawRows    = await conn.QueryAsync(config.Template, parameters);
        var rows       = ConvertRows(rawRows);

        return new AdapterResult
        {
            Rows      = rows,
            TotalRows = rows.Count,
        };
    }

    // Shared helpers used by TimescaleAdapter as well.

    protected static DynamicParameters BuildParameters(
        IReadOnlyDictionary<string, JsonElement> filters)
    {
        var p = new DynamicParameters();
        foreach (var (key, value) in filters)
            p.Add(key, GetScalar(value));
        return p;
    }

    protected static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> ConvertRows(
        IEnumerable<dynamic> rawRows)
    {
        return rawRows
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
    }

    private static object? GetScalar(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? (object)i : el.GetDouble(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Null   => null,
        _ => el.GetRawText()
    };

    private static readonly JsonElement NullElement = CreateNullElement();

    private static JsonElement CreateNullElement()
    {
        using var doc = JsonDocument.Parse("null");
        return doc.RootElement.Clone();
    }

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
            _                  => JsonSerializer.SerializeToElement(value.ToString() ?? string.Empty)
        };
    }

    protected static DatasourceConfig ParseConfig(DatasourceDefinition def) =>
        JsonSerializer.Deserialize(
            def.ConnectionConfig,
            AdaptersJsonContext.Default.DatasourceConfig)
        ?? throw new AdapterException("INVALID_CONNECTION_CONFIG", def.DatasourceId);
}
