using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using ReportingPlatform.ExcelProvider.Config;

namespace ReportingPlatform.ExcelProvider.Database;

/// <summary>
/// Runs at startup to ensure the provider and its four operations are registered in the platform DB.
/// Uses ON CONFLICT DO NOTHING so it is safe to run on every restart.
/// </summary>
public sealed class ProviderSeeder
{
    private readonly string _connectionString;
    private readonly ProviderOptions _opts;
    private readonly ILogger<ProviderSeeder> _logger;

    private static readonly string[] OperationPatterns =
    [
        "report.dashboard.summary",
        "report.sales.trend",
        "report.inventory.status",
        "report.regional.performance",
    ];

    public ProviderSeeder(
        string connectionString,
        IOptions<ProviderOptions> opts,
        ILogger<ProviderSeeder> logger)
    {
        _connectionString = connectionString;
        _opts             = opts.Value;
        _logger           = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Running provider seed check…");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // ── 1. Seed provider_registry ─────────────────────────────────────────
        var secretHash = BCrypt.Net.BCrypt.HashPassword(_opts.ClientSecret);

        await using var providerCmd = conn.CreateCommand();
        providerCmd.CommandText = """
            INSERT INTO provider_registry
                (provider_id, display_name, description, client_id, client_secret_hash,
                 operations, timeout_ms, status)
            VALUES
                (@providerId, @displayName, @description, @clientId, @secretHash,
                 @operations, @timeoutMs, 'active')
            ON CONFLICT (provider_id) DO NOTHING;
            """;

        providerCmd.Parameters.AddWithValue("@providerId",   _opts.ProviderId);
        providerCmd.Parameters.AddWithValue("@displayName",  "Excel Data Provider");
        providerCmd.Parameters.AddWithValue("@description",  "Reads SalesData.xlsx and serves dashboard, trend, inventory and regional reports.");
        providerCmd.Parameters.AddWithValue("@clientId",     _opts.ClientId);
        providerCmd.Parameters.AddWithValue("@secretHash",   secretHash);
        providerCmd.Parameters.AddWithValue("@operations",   OperationPatterns);
        providerCmd.Parameters.AddWithValue("@timeoutMs",    60_000);

        var providerRows = await providerCmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("provider_registry seed: {Rows} row(s) inserted", providerRows);

        // ── 2. Seed operation_registry ────────────────────────────────────────
        foreach (var pattern in OperationPatterns)
        {
            (string paramsSchema, string payloadSchema) = BuildSchemas(pattern);

            await using var opCmd = conn.CreateCommand();
            opCmd.CommandText = """
                INSERT INTO operation_registry
                    (operation_pattern, handler_type, provider_id,
                     params_schema, payload_schema,
                     timeout_ms, cacheable, cache_ttl_seconds)
                VALUES
                    (@pattern, 'provider', @providerId,
                     @paramsSchema::jsonb, @payloadSchema::jsonb,
                     60000, true, 300)
                ON CONFLICT (operation_pattern) DO NOTHING;
                """;

            opCmd.Parameters.AddWithValue("@pattern",       pattern);
            opCmd.Parameters.AddWithValue("@providerId",    _opts.ProviderId);
            opCmd.Parameters.AddWithValue("@paramsSchema",  paramsSchema);
            opCmd.Parameters.AddWithValue("@payloadSchema", payloadSchema);

            var opRows = await opCmd.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("operation_registry seed [{Pattern}]: {Rows} row(s) inserted", pattern, opRows);
        }

        _logger.LogInformation("Provider seed complete");
    }

    private static (string paramsSchema, string payloadSchema) BuildSchemas(string pattern) =>
        pattern switch
        {
            "report.dashboard.summary" => (
                """{"type":"object","properties":{"date":{"type":"string","format":"date"}},"additionalProperties":false}""",
                """{"type":"object","properties":{"totalRevenue":{"type":"number"},"totalUnits":{"type":"number"},"topRegion":{"type":"string"},"topProduct":{"type":"string"},"revenueByChannel":{"type":"object"},"alerts":{"type":"array","items":{"type":"string"}}}}"""
            ),
            "report.sales.trend" => (
                """{"type":"object","required":["fromDate","toDate","groupBy"],"properties":{"fromDate":{"type":"string","format":"date"},"toDate":{"type":"string","format":"date"},"groupBy":{"type":"string","enum":["day","week","month"]}}}""",
                """{"type":"object","properties":{"labels":{"type":"array","items":{"type":"string"}},"series":{"type":"array"}}}"""
            ),
            "report.inventory.status" => (
                """{"type":"object","additionalProperties":false}""",
                """{"type":"object","properties":{"products":{"type":"array"},"summary":{"type":"object"}}}"""
            ),
            "report.regional.performance" => (
                """{"type":"object","required":["period"],"properties":{"period":{"type":"string","enum":["today","week","month"]}}}""",
                """{"type":"object","properties":{"regions":{"type":"array"}}}"""
            ),
            _ => ("{}", "{}")
        };
}
