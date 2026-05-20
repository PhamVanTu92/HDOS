using Json.Schema;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Dapper;

namespace ReportingPlatform.IngestionApi.Services;

/// <summary>
/// Validates <c>IngestEventEnvelope.Payload</c> against a tenant-specific JSON Schema
/// fetched from the <c>event_schemas</c> table.
///
/// Compiled schemas are cached in IMemoryCache keyed by <c>"schema:{tenantId}:{eventType}"</c>
/// with a 10-minute sliding TTL (§1.5.1 — Patch 1). Cache miss triggers a DB read + compile.
/// If no row exists for the (tenantId, eventType) pair, validation is skipped (returns true).
/// </summary>
internal sealed class SchemaValidationService : ISchemaValidator
{
    private readonly string _connectionString;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SchemaValidationService> _logger;

    private static readonly MemoryCacheEntryOptions CacheOptions = new MemoryCacheEntryOptions()
        .SetSlidingExpiration(TimeSpan.FromMinutes(10));

    // Sentinel value stored in cache when no schema row exists — avoids DB round-trip on repeat.
    private static readonly JsonSchema NoSchema = JsonSchema.Empty;

    public SchemaValidationService(
        string connectionString,
        IMemoryCache cache,
        ILogger<SchemaValidationService> logger)
    {
        _connectionString = connectionString;
        _cache            = cache;
        _logger           = logger;
    }

    /// <summary>
    /// Returns null if validation passes (or no schema is registered).
    /// Returns a validation error message if the payload fails schema validation.
    /// </summary>
    public async Task<string?> ValidateAsync(
        string tenantId, string eventType, JsonElement payload, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(tenantId, eventType, ct);

        if (ReferenceEquals(schema, NoSchema))
            return null; // No schema registered — accept without validation.

        // OutputFormat.List (draft 2020-12) gives a flat list of annotation/error nodes.
        var result = schema.Evaluate(payload, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (result.IsValid)
            return null;

        var details = result.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => d.Errors?.Select(e => e.Value) ?? [])
            .FirstOrDefault() ?? "Payload does not match the registered JSON Schema.";

        return details;
    }

    private async Task<JsonSchema> GetSchemaAsync(string tenantId, string eventType, CancellationToken ct)
    {
        var key = $"schema:{tenantId}:{eventType}";

        if (_cache.TryGetValue(key, out JsonSchema? cached) && cached is not null)
            return cached;

        string? schemaJson = null;
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            schemaJson = await conn.QuerySingleOrDefaultAsync<string>(
                """
                SELECT schema_body::text
                FROM   event_schemas
                WHERE  tenant_id  = @tenantId
                  AND  event_type = @eventType
                """,
                new { tenantId, eventType });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load event schema for {TenantId}/{EventType}; skipping validation",
                tenantId, eventType);
            return NoSchema;
        }

        JsonSchema schema;
        if (schemaJson is null)
        {
            schema = NoSchema; // No schema row — cache the sentinel.
        }
        else
        {
            try { schema = JsonSchema.FromText(schemaJson); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse event schema for {TenantId}/{EventType}; skipping validation",
                    tenantId, eventType);
                return NoSchema;
            }
        }

        _cache.Set(key, schema, CacheOptions);
        return schema;
    }
}
