namespace ReportingPlatform.Adapters.Implementations;

/// <summary>
/// Adapter for datasources with mode="timescale".
/// Extends <see cref="SqlRawAdapter"/> with TimescaleDB-aware parameter injection:
/// <list type="bullet">
///   <item><description>@_bucket_width — time-bucket interval (default "1 hour")</description></item>
///   <item><description>@_from / @_to — resolved from filters["_time_from"] / filters["_time_to"]</description></item>
/// </list>
/// Templates should use standard @paramName placeholders; these special names are
/// injected automatically so templates do not need to special-case them.
/// </summary>
internal sealed class TimescaleAdapter : SqlRawAdapter
{
    private readonly ILogger<TimescaleAdapter> _logger;

    public TimescaleAdapter(NpgsqlDataSource db, ILogger<TimescaleAdapter> logger)
        : base(db, logger)
    {
        _logger = logger;
    }

    public override async Task<AdapterResult> FetchAsync(
        AdapterRequest request, CancellationToken ct = default)
    {
        var config = ParseConfig(request.Datasource);

        if (string.IsNullOrWhiteSpace(config.Template))
            throw new AdapterException(
                "MISSING_TEMPLATE",
                $"datasource '{request.Datasource.DatasourceId}' has no 'template' in ConnectionConfig");

        _logger.LogDebug(
            "TimescaleAdapter: tenant={TenantId} datasource={DatasourceId}",
            request.TenantId, request.Datasource.DatasourceId);

        await using var conn = await Db.OpenConnectionAsync(ct);

        var parameters = BuildParameters(request.Filters);
        InjectTimescaleParams(parameters, request.Filters);

        var rawRows = await conn.QueryAsync(config.Template, parameters);
        var rows    = ConvertRows(rawRows);

        return new AdapterResult
        {
            Rows      = rows,
            TotalRows = rows.Count,
        };
    }

    /// <summary>
    /// Adds standard TimescaleDB convenience parameters if not already present.
    /// Templates that do not reference these parameters are unaffected.
    /// </summary>
    private static void InjectTimescaleParams(
        DynamicParameters p,
        IReadOnlyDictionary<string, JsonElement> filters)
    {
        // @_bucket_width — e.g. "1 hour", "15 minutes"
        if (!filters.ContainsKey("_bucket_width"))
            p.Add("_bucket_width", "1 hour");

        // @_from / @_to — time-range boundaries
        if (!filters.ContainsKey("_from"))
            p.Add("_from", DateTime.UtcNow.AddDays(-7));
        if (!filters.ContainsKey("_to"))
            p.Add("_to", DateTime.UtcNow);
    }
}
