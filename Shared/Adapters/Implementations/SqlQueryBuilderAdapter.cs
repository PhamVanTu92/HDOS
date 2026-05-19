namespace ReportingPlatform.Adapters.Implementations;

/// <summary>
/// Adapter for datasources with mode="querybuilder".
/// Delegates to <see cref="SqlKataQueryBuilder"/> which enforces the column/sort whitelist.
/// </summary>
internal sealed class SqlQueryBuilderAdapter : IDatasourceAdapter
{
    private readonly SqlKataQueryBuilder _builder;
    private readonly ILogger<SqlQueryBuilderAdapter> _logger;

    public SqlQueryBuilderAdapter(
        SqlKataQueryBuilder builder,
        ILogger<SqlQueryBuilderAdapter> logger)
    {
        _builder = builder;
        _logger  = logger;
    }

    public async Task<AdapterResult> FetchAsync(AdapterRequest request, CancellationToken ct = default)
    {
        var config = ParseConfig(request.Datasource);

        if (config.Source is null)
            throw new AdapterException(
                "MISSING_SOURCE",
                $"datasource '{request.Datasource.DatasourceId}' has no 'source' in ConnectionConfig");

        // Table widgets supply their own pagination; other widgets use a safe default
        // that returns the first page and caps at the QueryableSource.MaxRows.
        var pagination = request.Pagination ?? new TablePaginationParams
        {
            Page     = 1,
            PageSize = 25,
        };

        _logger.LogDebug(
            "QueryBuilderAdapter: tenant={TenantId} source={Source} page={Page}",
            request.TenantId, config.Source, pagination.Page);

        var result = await _builder.ExecuteAsync(
            request.TenantId,
            config.Source,
            pagination,
            ct);

        return new AdapterResult
        {
            Rows      = result.Rows,
            TotalRows = result.TotalRows,
        };
    }

    private static DatasourceConfig ParseConfig(DatasourceDefinition def) =>
        JsonSerializer.Deserialize(
            def.ConnectionConfig,
            AdaptersJsonContext.Default.DatasourceConfig)
        ?? throw new AdapterException("INVALID_CONNECTION_CONFIG", def.DatasourceId);
}
