using Npgsql;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// Admin CRUD for config-driven modules, tabs, and widgets.
/// Requires role: admin.
/// </summary>
[ApiController]
[Route("api/v1/admin/modules")]
[Authorize(Roles = "admin")]
public sealed class AdminModuleController : ControllerBase
{
    private readonly NpgsqlDataSource _db;

    public AdminModuleController(NpgsqlDataSource db) => _db = db;

    // ── GET /api/v1/admin/module-groups ─────────────────────────────────────

    [HttpGet("/api/v1/admin/module-groups")]
    public async Task<IActionResult> ListGroupsAsync(CancellationToken ct)
    {
        var rows = new List<object>();
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, label, icon, sort_order
            FROM module_groups
            ORDER BY sort_order, label
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new
            {
                id        = r.GetGuid(0),
                slug      = r.GetString(1),
                label     = r.GetString(2),
                icon      = r.IsDBNull(3) ? null : r.GetString(3),
                sortOrder = r.GetInt32(4),
            });
        }
        return Ok(rows);
    }

    // ── GET /api/v1/admin/modules ────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var rows = new List<object>();
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.slug, m.label, m.icon, m.description,
                   m.required_roles, m.sort_order, m.is_visible, m.is_active,
                   m.refresh_interval_seconds, m.created_at,
                   g.slug AS group_slug, g.label AS group_label
            FROM modules m
            JOIN module_groups g ON g.id = m.group_id
            ORDER BY g.sort_order, m.sort_order
            """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new
            {
                id                     = r.GetGuid(0),
                slug                   = r.GetString(1),
                label                  = r.GetString(2),
                icon                   = r.IsDBNull(3) ? null : r.GetString(3),
                description            = r.IsDBNull(4) ? null : r.GetString(4),
                requiredRoles          = r.IsDBNull(5) ? null : (string[])r.GetValue(5),
                sortOrder              = r.GetInt32(6),
                isVisible              = r.GetBoolean(7),
                isActive               = r.GetBoolean(8),
                refreshIntervalSeconds = r.IsDBNull(9) ? (int?)null : r.GetInt32(9),
                createdAt              = r.GetDateTime(10),
                groupSlug              = r.GetString(11),
                groupLabel             = r.GetString(12),
            });
        }
        return Ok(rows);
    }

    // ── POST /api/v1/admin/modules ───────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] UpsertModuleRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Slug))
            return BadRequest(new { error = "slug is required." });
        if (string.IsNullOrWhiteSpace(req.Label))
            return BadRequest(new { error = "label is required." });
        if (req.GroupId == Guid.Empty)
            return BadRequest(new { error = "groupId is required." });

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO modules (group_id, slug, label, icon, description, required_roles, sort_order, is_visible, refresh_interval_seconds)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            RETURNING id, slug, label, created_at
            """;
        cmd.Parameters.AddWithValue(req.GroupId);
        cmd.Parameters.AddWithValue(req.Slug.Trim());
        cmd.Parameters.AddWithValue(req.Label.Trim());
        cmd.Parameters.AddWithValue(req.Icon is null ? DBNull.Value : (object)req.Icon);
        cmd.Parameters.AddWithValue(req.Description is null ? DBNull.Value : (object)req.Description);
        cmd.Parameters.AddWithValue(req.RequiredRoles is null ? DBNull.Value : (object)req.RequiredRoles);
        cmd.Parameters.AddWithValue(req.SortOrder);
        cmd.Parameters.AddWithValue(req.IsVisible);
        cmd.Parameters.AddWithValue(req.RefreshIntervalSeconds.HasValue ? (object)req.RefreshIntervalSeconds.Value : DBNull.Value);

        try
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return StatusCode(500, new { error = "Insert failed." });
            return Created($"/api/v1/admin/modules/{r.GetString(1)}",
                new { id = r.GetGuid(0), slug = r.GetString(1), label = r.GetString(2), createdAt = r.GetDateTime(3) });
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { error = $"Module slug '{req.Slug}' already exists." });
        }
    }

    // ── PUT /api/v1/admin/modules/{slug} ─────────────────────────────────────

    [HttpPut("{slug}")]
    public async Task<IActionResult> UpdateAsync(
        string slug,
        [FromBody] UpsertModuleRequest req,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE modules SET
                label                  = $2,
                icon                   = $3,
                description            = $4,
                required_roles         = $5,
                sort_order             = $6,
                is_visible             = $7,
                is_active              = $8,
                refresh_interval_seconds = $9,
                updated_at             = NOW()
            WHERE slug = $1
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(slug);
        cmd.Parameters.AddWithValue(req.Label?.Trim() ?? slug);
        cmd.Parameters.AddWithValue(req.Icon is null ? DBNull.Value : (object)req.Icon);
        cmd.Parameters.AddWithValue(req.Description is null ? DBNull.Value : (object)req.Description);
        cmd.Parameters.AddWithValue(req.RequiredRoles is null ? DBNull.Value : (object)req.RequiredRoles);
        cmd.Parameters.AddWithValue(req.SortOrder);
        cmd.Parameters.AddWithValue(req.IsVisible);
        cmd.Parameters.AddWithValue(req.IsActive);
        cmd.Parameters.AddWithValue(req.RefreshIntervalSeconds.HasValue ? (object)req.RefreshIntervalSeconds.Value : DBNull.Value);

        var updated = await cmd.ExecuteScalarAsync(ct);
        if (updated is null)
            return NotFound(new { error = $"Module '{slug}' not found." });
        return Ok(new { id = updated, slug });
    }

    // ── DELETE /api/v1/admin/modules/{slug} ──────────────────────────────────

    [HttpDelete("{slug}")]
    public async Task<IActionResult> DeleteAsync(string slug, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM modules WHERE slug = $1 RETURNING id";
        cmd.Parameters.AddWithValue(slug);
        var deleted = await cmd.ExecuteScalarAsync(ct);
        if (deleted is null)
            return NotFound(new { error = $"Module '{slug}' not found." });
        return NoContent();
    }

    // ── GET /api/v1/admin/modules/{slug}/tabs ────────────────────────────────

    [HttpGet("{slug}/tabs")]
    public async Task<IActionResult> GetTabsAsync(string slug, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.slug, t.label, t.sort_order, t.is_default,
                   COUNT(w.id) AS widget_count
            FROM module_tabs t
            JOIN modules m ON m.id = t.module_id
            LEFT JOIN widgets w ON w.tab_id = t.id
            WHERE m.slug = $1
            GROUP BY t.id, t.slug, t.label, t.sort_order, t.is_default
            ORDER BY t.sort_order
            """;
        cmd.Parameters.AddWithValue(slug);

        var rows = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new
            {
                id          = r.GetGuid(0),
                slug        = r.GetString(1),
                label       = r.GetString(2),
                sortOrder   = r.GetInt32(3),
                isDefault   = r.GetBoolean(4),
                widgetCount = r.GetInt64(5),
            });
        }
        return Ok(rows);
    }

    // ── POST /api/v1/admin/modules/{slug}/tabs ───────────────────────────────

    [HttpPost("{slug}/tabs")]
    public async Task<IActionResult> AddTabAsync(
        string slug,
        [FromBody] UpsertTabRequest req,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        // Get module id
        Guid moduleId;
        await using (var getCmd = conn.CreateCommand())
        {
            getCmd.CommandText = "SELECT id FROM modules WHERE slug = $1";
            getCmd.Parameters.AddWithValue(slug);
            var result = await getCmd.ExecuteScalarAsync(ct);
            if (result is null) return NotFound(new { error = $"Module '{slug}' not found." });
            moduleId = (Guid)result;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO module_tabs (module_id, slug, label, sort_order, is_default)
            VALUES ($1, $2, $3, $4, $5)
            RETURNING id, slug, label
            """;
        cmd.Parameters.AddWithValue(moduleId);
        cmd.Parameters.AddWithValue(req.Slug.Trim());
        cmd.Parameters.AddWithValue(req.Label.Trim());
        cmd.Parameters.AddWithValue(req.SortOrder);
        cmd.Parameters.AddWithValue(req.IsDefault);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return StatusCode(500);
        return Created("", new { id = r.GetGuid(0), slug = r.GetString(1), label = r.GetString(2) });
    }

    // ── PUT /api/v1/admin/modules/{slug}/tabs/{tabId}/widgets ─────────────────
    // Upsert widget layout for a tab (full replacement — react-grid-layout saves whole canvas)

    [HttpPut("{slug}/tabs/{tabId:guid}/widgets")]
    public async Task<IActionResult> SaveWidgetsAsync(
        string slug,
        Guid tabId,
        [FromBody] List<UpsertWidgetRequest> widgets,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx   = await conn.BeginTransactionAsync(ct);

        // Verify tab belongs to this module
        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = """
                SELECT 1 FROM module_tabs t
                JOIN modules m ON m.id = t.module_id
                WHERE t.id = $1 AND m.slug = $2
                """;
            checkCmd.Parameters.AddWithValue(tabId);
            checkCmd.Parameters.AddWithValue(slug);
            var exists = await checkCmd.ExecuteScalarAsync(ct);
            if (exists is null)
                return NotFound(new { error = "Tab not found or does not belong to this module." });
        }

        // Delete all existing widgets for this tab
        await using (var delCmd = conn.CreateCommand())
        {
            delCmd.CommandText = "DELETE FROM widgets WHERE tab_id = $1";
            delCmd.Parameters.AddWithValue(tabId);
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        // Insert new widgets
        var inserted = 0;
        foreach (var w in widgets)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO widgets
                    (tab_id, widget_key, title, subtitle, chart_type,
                     grid_x, grid_y, grid_w, grid_h,
                     operation_pattern, provider_id, params_template, visual_config,
                     filter_bindings, interactions, filter_key, sort_order)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12::jsonb,$13::jsonb,$14,$15::jsonb,$16,$17)
                """;
            cmd.Parameters.AddWithValue(tabId);
            cmd.Parameters.AddWithValue(w.WidgetKey);
            cmd.Parameters.AddWithValue(w.Title is null ? DBNull.Value : (object)w.Title);
            cmd.Parameters.AddWithValue(w.Subtitle is null ? DBNull.Value : (object)w.Subtitle);
            cmd.Parameters.AddWithValue(w.ChartType);
            cmd.Parameters.AddWithValue(w.GridX);
            cmd.Parameters.AddWithValue(w.GridY);
            cmd.Parameters.AddWithValue(w.GridW);
            cmd.Parameters.AddWithValue(w.GridH);
            cmd.Parameters.AddWithValue(w.OperationPattern is null ? DBNull.Value : (object)w.OperationPattern);
            cmd.Parameters.AddWithValue(w.ProviderId is null ? DBNull.Value : (object)w.ProviderId);
            cmd.Parameters.AddWithValue(string.IsNullOrEmpty(w.ParamsTemplate) ? "{}" : w.ParamsTemplate);
            cmd.Parameters.AddWithValue(string.IsNullOrEmpty(w.VisualConfig) ? "{}" : w.VisualConfig);
            cmd.Parameters.AddWithValue(w.FilterBindings is null ? Array.Empty<string>() : w.FilterBindings);
            cmd.Parameters.AddWithValue(string.IsNullOrEmpty(w.Interactions) ? "{}" : w.Interactions);
            cmd.Parameters.AddWithValue(w.FilterKey is null ? DBNull.Value : (object)w.FilterKey);
            cmd.Parameters.AddWithValue(inserted);
            await cmd.ExecuteNonQueryAsync(ct);
            inserted++;
        }

        await tx.CommitAsync(ct);
        return Ok(new { saved = inserted });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record UpsertModuleRequest
{
    public Guid   GroupId              { get; init; }
    public required string Slug        { get; init; }
    public required string Label       { get; init; }
    public string? Icon                { get; init; }
    public string? Description         { get; init; }
    public string[]? RequiredRoles     { get; init; }
    public int    SortOrder            { get; init; } = 0;
    public bool   IsVisible            { get; init; } = true;
    public bool   IsActive             { get; init; } = true;
    public int?   RefreshIntervalSeconds { get; init; }
}

public sealed record UpsertTabRequest
{
    public required string Slug  { get; init; }
    public required string Label { get; init; }
    public int  SortOrder        { get; init; } = 0;
    public bool IsDefault        { get; init; } = false;
}

public sealed record UpsertWidgetRequest
{
    public required string WidgetKey  { get; init; }
    public string? Title              { get; init; }
    public string? Subtitle           { get; init; }
    public required string ChartType  { get; init; }
    public int  GridX                 { get; init; }
    public int  GridY                 { get; init; }
    public int  GridW                 { get; init; } = 6;
    public int  GridH                 { get; init; } = 4;
    public string? OperationPattern   { get; init; }
    public string? ProviderId         { get; init; }
    public string? ParamsTemplate     { get; init; }
    public string? VisualConfig       { get; init; }
    public string[]? FilterBindings   { get; init; }
    public string? Interactions       { get; init; }
    public string? FilterKey          { get; init; }
}
