using ReportingPlatform.Adapters.Implementations;

namespace ReportingPlatform.Adapters.Factory;

/// <summary>
/// Resolves the appropriate <see cref="IDatasourceAdapter"/> for a given
/// <see cref="DatasourceDefinition"/> based on its type and connection config mode.
/// </summary>
public sealed class DatasourceAdapterFactory : IDatasourceAdapterFactory
{
    private readonly SqlQueryBuilderAdapter _queryBuilder;
    private readonly SqlRawAdapter          _raw;
    private readonly TimescaleAdapter       _timescale;
    private readonly ExternalProviderAdapter _externalProvider;

    // Internal constructor: types are internal to Adapters assembly.
    // DI resolves via reflection so visibility is not required.
    internal DatasourceAdapterFactory(
        SqlQueryBuilderAdapter   queryBuilder,
        SqlRawAdapter            raw,
        TimescaleAdapter         timescale,
        ExternalProviderAdapter  externalProvider)
    {
        _queryBuilder     = queryBuilder;
        _raw              = raw;
        _timescale        = timescale;
        _externalProvider = externalProvider;
    }

    /// <summary>
    /// Returns the adapter for <paramref name="definition"/>.
    /// Throws <see cref="AdapterException"/> with code <c>ADAPTER_NOT_SUPPORTED</c>
    /// for unknown datasource types.
    /// </summary>
    public IDatasourceAdapter Resolve(DatasourceDefinition definition)
    {
        if (definition.Type.Equals("external_provider", StringComparison.OrdinalIgnoreCase))
        {
            // Validate config at Resolve() time — fail fast at dashboard load, not per-render.
            ValidateExternalProviderConfig(definition);
            return _externalProvider;
        }

        if (!definition.Type.Equals("sql", StringComparison.OrdinalIgnoreCase))
            throw new AdapterException("ADAPTER_NOT_SUPPORTED", definition.Type);

        // For sql type, inspect the "mode" field of ConnectionConfig.
        string? mode = null;
        if (definition.ConnectionConfig.ValueKind == JsonValueKind.Object &&
            definition.ConnectionConfig.TryGetProperty("mode", out var modeProp))
        {
            mode = modeProp.GetString();
        }

        return mode switch
        {
            "querybuilder" => _queryBuilder,
            "raw"          => _raw,
            "timescale"    => _timescale,
            _ => throw new AdapterException(
                "UNKNOWN_SQL_MODE",
                $"datasource '{definition.DatasourceId}' mode='{mode ?? "<null>"}'")
        };
    }

    private static void ValidateExternalProviderConfig(DatasourceDefinition definition)
    {
        if (definition.ConnectionConfig.ValueKind != JsonValueKind.Object ||
            !definition.ConnectionConfig.TryGetProperty("operationName", out var opEl) ||
            opEl.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(opEl.GetString()))
        {
            throw new AdapterException("PROVIDER_CONFIG_INVALID",
                $"datasource '{definition.DatasourceId}': connectionConfig.operationName is required for external_provider.");
        }

        if (!definition.ConnectionConfig.TryGetProperty("paramMapping", out var pmEl) ||
            pmEl.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterException("PROVIDER_CONFIG_INVALID",
                $"datasource '{definition.DatasourceId}': connectionConfig.paramMapping must be an object.");
        }
    }
}
