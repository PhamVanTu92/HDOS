using Npgsql;
using ReportingPlatform.Providers.Abstractions;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// Admin endpoints for operation_registry management.
/// Requires role: admin (enforced via [Authorize(Roles = "admin")]).
/// </summary>
[ApiController]
[Route("api/v1/admin/operations")]
[Authorize(Roles = "admin")]
public sealed class AdminOperationsController : ControllerBase
{
    private readonly NpgsqlDataSource  _db;
    private readonly IOperationRegistry _registry;
    private readonly ILogger<AdminOperationsController> _logger;

    public AdminOperationsController(
        NpgsqlDataSource db,
        IOperationRegistry registry,
        ILogger<AdminOperationsController> logger)
    {
        _db       = db;
        _registry = registry;
        _logger   = logger;
    }

    // ── GET /api/v1/admin/operations ─────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var rows = new List<object>();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, operation_pattern, handler_type, provider_id,
                   params_schema::text, timeout_ms, cacheable, cache_ttl_seconds,
                   idempotent, status, created_at, updated_at
            FROM operation_registry
            ORDER BY operation_pattern
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new
            {
                id               = reader.GetInt64(0),
                operationPattern = reader.GetString(1),
                handlerType      = reader.GetString(2),
                providerId       = reader.IsDBNull(3) ? null : reader.GetString(3),
                paramsSchema     = reader.IsDBNull(4) ? null : reader.GetString(4),
                timeoutMs        = reader.GetInt32(5),
                cacheable        = reader.GetBoolean(6),
                cacheTtlSeconds  = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
                idempotent       = reader.GetBoolean(8),
                status           = reader.GetString(9),
                createdAt        = reader.GetDateTime(10),
                updatedAt        = reader.GetDateTime(11),
            });
        }

        return Ok(rows);
    }

    // ── POST /api/v1/admin/operations ────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> AddAsync(
        [FromBody] AddOperationRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.OperationPattern))
            return BadRequest(new { error = "operationPattern is required." });

        if (!string.IsNullOrWhiteSpace(req.ParamsSchemaJson))
        {
            try { System.Text.Json.JsonDocument.Parse(req.ParamsSchemaJson); }
            catch { return BadRequest(new { error = "paramsSchemaJson is not valid JSON." }); }
        }

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO operation_registry
                (operation_pattern, handler_type, provider_id, params_schema,
                 timeout_ms, cacheable, cache_ttl_seconds, idempotent)
            VALUES ($1, $2, $3, $4::jsonb, $5, $6, $7, $8)
            ON CONFLICT (operation_pattern) DO NOTHING
            RETURNING id, operation_pattern, handler_type, provider_id,
                      params_schema::text, timeout_ms, cacheable, cache_ttl_seconds,
                      idempotent, status, created_at, updated_at
            """;
        cmd.Parameters.AddWithValue(req.OperationPattern.Trim());
        cmd.Parameters.AddWithValue(string.IsNullOrWhiteSpace(req.HandlerType) ? "provider" : req.HandlerType.Trim());
        cmd.Parameters.AddWithValue(req.ProviderId is null ? DBNull.Value : (object)req.ProviderId.Trim());
        cmd.Parameters.AddWithValue(string.IsNullOrWhiteSpace(req.ParamsSchemaJson) ? DBNull.Value : (object)req.ParamsSchemaJson);
        cmd.Parameters.AddWithValue(req.TimeoutMs > 0 ? req.TimeoutMs : 30_000);
        cmd.Parameters.AddWithValue(req.Cacheable);
        cmd.Parameters.AddWithValue(req.CacheTtlSeconds.HasValue ? (object)req.CacheTtlSeconds.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.Idempotent);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return Conflict(new { error = $"Operation pattern '{req.OperationPattern}' already exists." });
        }

        var result = ReadRow(reader);
        await reader.CloseAsync();

        await _registry.ReloadAsync(ct);

        return Created($"/api/v1/admin/operations/{Uri.EscapeDataString(req.OperationPattern)}", result);
    }

    // ── PUT /api/v1/admin/operations/{**pattern} ─────────────────────────────

    [HttpPut("{**pattern}")]
    public async Task<IActionResult> UpdateAsync(
        string pattern,
        [FromBody] UpdateOperationRequest req,
        CancellationToken ct)
    {
        var decoded = Uri.UnescapeDataString(pattern);

        if (!string.IsNullOrWhiteSpace(req.ParamsSchemaJson))
        {
            try { System.Text.Json.JsonDocument.Parse(req.ParamsSchemaJson); }
            catch { return BadRequest(new { error = "paramsSchemaJson is not valid JSON." }); }
        }

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE operation_registry SET
                handler_type      = $2,
                provider_id       = $3,
                params_schema     = $4::jsonb,
                timeout_ms        = $5,
                cacheable         = $6,
                cache_ttl_seconds = $7,
                idempotent        = $8,
                status            = $9,
                updated_at        = NOW()
            WHERE operation_pattern = $1
            RETURNING id, operation_pattern, handler_type, provider_id,
                      params_schema::text, timeout_ms, cacheable, cache_ttl_seconds,
                      idempotent, status, created_at, updated_at
            """;
        cmd.Parameters.AddWithValue(decoded);
        cmd.Parameters.AddWithValue(string.IsNullOrWhiteSpace(req.HandlerType) ? "provider" : req.HandlerType.Trim());
        cmd.Parameters.AddWithValue(req.ProviderId is null ? DBNull.Value : (object)req.ProviderId.Trim());
        cmd.Parameters.AddWithValue(string.IsNullOrWhiteSpace(req.ParamsSchemaJson) ? DBNull.Value : (object)req.ParamsSchemaJson);
        cmd.Parameters.AddWithValue(req.TimeoutMs > 0 ? req.TimeoutMs : 30_000);
        cmd.Parameters.AddWithValue(req.Cacheable);
        cmd.Parameters.AddWithValue(req.CacheTtlSeconds.HasValue ? (object)req.CacheTtlSeconds.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.Idempotent);
        cmd.Parameters.AddWithValue(string.IsNullOrWhiteSpace(req.Status) ? "active" : req.Status.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return NotFound(new { error = $"Operation pattern '{decoded}' not found." });

        var result = ReadRow(reader);
        await reader.CloseAsync();

        await _registry.ReloadAsync(ct);

        return Ok(result);
    }

    // ── DELETE /api/v1/admin/operations/{**pattern} ──────────────────────────

    [HttpDelete("{**pattern}")]
    public async Task<IActionResult> DeleteAsync(string pattern, CancellationToken ct)
    {
        var decoded = Uri.UnescapeDataString(pattern);

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM operation_registry
            WHERE operation_pattern = $1
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(decoded);

        var deleted = await cmd.ExecuteScalarAsync(ct);
        if (deleted is null)
            return NotFound(new { error = $"Operation pattern '{decoded}' not found." });

        await _registry.ReloadAsync(ct);

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static object ReadRow(NpgsqlDataReader r) => new
    {
        id               = r.GetInt64(0),
        operationPattern = r.GetString(1),
        handlerType      = r.GetString(2),
        providerId       = r.IsDBNull(3) ? null : r.GetString(3),
        paramsSchema     = r.IsDBNull(4) ? null : r.GetString(4),
        timeoutMs        = r.GetInt32(5),
        cacheable        = r.GetBoolean(6),
        cacheTtlSeconds  = r.IsDBNull(7) ? (int?)null : r.GetInt32(7),
        idempotent       = r.GetBoolean(8),
        status           = r.GetString(9),
        createdAt        = r.GetDateTime(10),
        updatedAt        = r.GetDateTime(11),
    };
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record AddOperationRequest
{
    public required string OperationPattern { get; init; }
    public string HandlerType { get; init; } = "provider";
    public string? ProviderId { get; init; }
    public string? ParamsSchemaJson { get; init; }   // raw JSON string
    public int TimeoutMs { get; init; } = 30000;
    public bool Cacheable { get; init; } = false;
    public int? CacheTtlSeconds { get; init; }
    public bool Idempotent { get; init; } = true;
}

public sealed record UpdateOperationRequest
{
    public required string HandlerType { get; init; }
    public string? ProviderId { get; init; }
    public string? ParamsSchemaJson { get; init; }
    public int TimeoutMs { get; init; } = 30000;
    public bool Cacheable { get; init; } = false;
    public int? CacheTtlSeconds { get; init; }
    public bool Idempotent { get; init; } = true;
    public string Status { get; init; } = "active";
}
