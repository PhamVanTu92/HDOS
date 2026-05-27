using Npgsql;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// Schema endpoints: widget type catalog + payload schema validation.
/// GET  /api/v1/admin/schemas          — list/get widget type schemas
/// POST /api/v1/admin/schemas/validate — validate payloadSchema for a chart type
/// </summary>
[ApiController]
[Route("api/v1/admin/schemas")]
[Authorize(Roles = "admin")]
public sealed class SchemaController : ControllerBase
{
    private readonly NpgsqlDataSource _db;

    public SchemaController(NpgsqlDataSource db) => _db = db;

    // ── GET /api/v1/admin/schemas ────────────────────────────────────────────
    // ?category=healthcare&chartType=kpi_grid

    [HttpGet]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? category,
        [FromQuery] string? chartType,
        CancellationToken ct)
    {
        var rows = new List<object>();
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();

        cmd.CommandText = """
            SELECT chart_type, category, label, description, icon,
                   row_schema::text, required_columns, optional_columns, compatible_with,
                   is_active, sort_order
            FROM widget_type_catalog
            WHERE ($1::text IS NULL OR category = $1)
              AND ($2::text IS NULL OR chart_type = $2)
              AND is_active = true
            ORDER BY sort_order, chart_type
            """;
        cmd.Parameters.AddWithValue(category is null ? DBNull.Value : (object)category);
        cmd.Parameters.AddWithValue(chartType is null ? DBNull.Value : (object)chartType);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new
            {
                chartType        = r.GetString(0),
                category         = r.GetString(1),
                label            = r.GetString(2),
                description      = r.IsDBNull(3) ? null : r.GetString(3),
                icon             = r.IsDBNull(4) ? null : r.GetString(4),
                rowSchema        = r.IsDBNull(5) ? "{}" : r.GetString(5),
                requiredColumns  = r.IsDBNull(6) ? Array.Empty<string>() : (string[])r.GetValue(6),
                optionalColumns  = r.IsDBNull(7) ? Array.Empty<string>() : (string[])r.GetValue(7),
                compatibleWith   = r.IsDBNull(8) ? Array.Empty<string>() : (string[])r.GetValue(8),
                sortOrder        = r.GetInt32(10),
            });
        }

        if (chartType is not null && rows.Count == 0)
            return NotFound(new { error = $"Chart type '{chartType}' not found." });

        return Ok(rows);
    }

    // ── POST /api/v1/admin/schemas/validate ──────────────────────────────────

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateAsync(
        [FromBody] ValidateSchemaRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ChartType))
            return BadRequest(new { error = "chartType is required." });

        // Validate req.PayloadSchema is parseable JSON
        if (!string.IsNullOrWhiteSpace(req.PayloadSchema))
        {
            try { System.Text.Json.JsonDocument.Parse(req.PayloadSchema); }
            catch { return BadRequest(new { error = "payloadSchema is not valid JSON." }); }
        }

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT required_columns, optional_columns
            FROM widget_type_catalog
            WHERE chart_type = $1 AND is_active = true
            """;
        cmd.Parameters.AddWithValue(req.ChartType);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return NotFound(new { error = $"Unknown chartType '{req.ChartType}'." });

        var required = r.IsDBNull(0) ? Array.Empty<string>() : (string[])r.GetValue(0);
        var optional = r.IsDBNull(1) ? Array.Empty<string>() : (string[])r.GetValue(1);

        // Simple validation: check if payloadSchema declares a "rows" array
        // and that required columns are mentioned as properties of items.
        var warnings = new List<string>();
        var errors   = new List<string>();

        if (!string.IsNullOrWhiteSpace(req.PayloadSchema) && required.Length > 0)
        {
            var schemaText = req.PayloadSchema;
            foreach (var col in required)
            {
                if (!schemaText.Contains($"\"{col}\"", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Required column '{col}' not found in payloadSchema properties.");
            }

            if (!schemaText.Contains("\"rows\"", StringComparison.OrdinalIgnoreCase))
                errors.Add("payloadSchema must define a 'rows' array property.");
        }

        return Ok(new
        {
            valid            = errors.Count == 0,
            errors,
            warnings,
            requiredColumns  = required,
            optionalColumns  = optional,
        });
    }
}

public sealed record ValidateSchemaRequest
{
    public required string ChartType { get; init; }
    public string? PayloadSchema { get; init; }
}
