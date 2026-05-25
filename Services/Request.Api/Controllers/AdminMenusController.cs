using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Npgsql;

namespace ReportingPlatform.RequestApi.Controllers;

[ApiController]
[Route("api/v1/admin/menus")]
[Authorize(Roles = "admin")]
public sealed class AdminMenusController : ControllerBase
{
    private readonly NpgsqlDataSource _db;

    public AdminMenusController(NpgsqlDataSource db) => _db = db;

    // ─── Menu CRUD ────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ListMenusAsync(CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.id, m.name, m.slug, m.icon, m.description,
                   m.parent_id, m.sort_order, m.is_visible,
                   m.created_at, m.updated_at,
                   COUNT(s.id) AS screen_count
            FROM menu_nodes m
            LEFT JOIN report_screens s ON s.menu_id = m.id
            GROUP BY m.id
            ORDER BY m.parent_id NULLS FIRST, m.sort_order
            """;

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<object>();
        while (await rdr.ReadAsync(ct))
        {
            rows.Add(new
            {
                id          = rdr.GetGuid(0),
                name        = rdr.GetString(1),
                slug        = rdr.GetString(2),
                icon        = rdr.GetString(3),
                description = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                parentId    = rdr.IsDBNull(5) ? (Guid?)null : rdr.GetGuid(5),
                sortOrder   = rdr.GetInt32(6),
                isVisible   = rdr.GetBoolean(7),
                createdAt   = rdr.GetDateTime(8),
                updatedAt   = rdr.GetDateTime(9),
                screenCount = rdr.GetInt64(10)
            });
        }
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> CreateMenuAsync([FromBody] MenuUpsertRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO menu_nodes (name, slug, icon, description, parent_id, sort_order, is_visible)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            RETURNING id, slug, created_at
            """;
        cmd.Parameters.AddWithValue(req.Name ?? "");
        cmd.Parameters.AddWithValue(req.Slug ?? Slugify(req.Name ?? ""));
        cmd.Parameters.AddWithValue(req.Icon ?? "📊");
        cmd.Parameters.AddWithValue(req.Description is null ? DBNull.Value : (object)req.Description);
        cmd.Parameters.AddWithValue(req.ParentId.HasValue ? (object)req.ParentId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.SortOrder ?? 0);
        cmd.Parameters.AddWithValue(req.IsVisible ?? true);

        try
        {
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            await rdr.ReadAsync(ct);
            return Created($"api/v1/admin/menus/{rdr.GetGuid(0)}", new
            {
                id        = rdr.GetGuid(0),
                slug      = rdr.GetString(1),
                createdAt = rdr.GetDateTime(2)
            });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { message = "Slug already exists" });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateMenuAsync(Guid id, [FromBody] MenuUpsertRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE menu_nodes SET
                name        = COALESCE($2, name),
                slug        = COALESCE($3, slug),
                icon        = COALESCE($4, icon),
                description = COALESCE($5, description),
                parent_id   = COALESCE($6, parent_id),
                sort_order  = COALESCE($7, sort_order),
                is_visible  = COALESCE($8, is_visible),
                updated_at  = NOW()
            WHERE id = $1
            RETURNING id, updated_at
            """;
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(req.Name is null ? DBNull.Value : (object)req.Name);
        cmd.Parameters.AddWithValue(req.Slug is null ? DBNull.Value : (object)req.Slug);
        cmd.Parameters.AddWithValue(req.Icon is null ? DBNull.Value : (object)req.Icon);
        cmd.Parameters.AddWithValue(req.Description is null ? DBNull.Value : (object)req.Description);
        cmd.Parameters.AddWithValue(req.ParentId.HasValue ? (object)req.ParentId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.SortOrder.HasValue ? (object)req.SortOrder.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.IsVisible.HasValue ? (object)req.IsVisible.Value : DBNull.Value);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return Ok(new { id = rdr.GetGuid(0), updatedAt = rdr.GetDateTime(1) });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteMenuAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM menu_nodes WHERE id = $1 RETURNING id";
        cmd.Parameters.AddWithValue(id);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return NoContent();
    }

    // ─── Screen CRUD ──────────────────────────────────────────────────────────

    [HttpGet("{menuId:guid}/screens")]
    public async Task<IActionResult> ListScreensAsync(Guid menuId, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.menu_id, s.name, s.icon, s.status, s.sort_order,
                   s.created_at, s.updated_at,
                   COUNT(w.id) AS widget_count
            FROM report_screens s
            LEFT JOIN screen_widgets w ON w.screen_id = s.id
            WHERE s.menu_id = $1
            GROUP BY s.id
            ORDER BY s.sort_order
            """;
        cmd.Parameters.AddWithValue(menuId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<object>();
        while (await rdr.ReadAsync(ct))
        {
            rows.Add(new
            {
                id          = rdr.GetGuid(0),
                menuId      = rdr.GetGuid(1),
                name        = rdr.GetString(2),
                icon        = rdr.GetString(3),
                status      = rdr.GetString(4),
                sortOrder   = rdr.GetInt32(5),
                createdAt   = rdr.GetDateTime(6),
                updatedAt   = rdr.GetDateTime(7),
                widgetCount = rdr.GetInt64(8)
            });
        }
        return Ok(rows);
    }

    [HttpPost("{menuId:guid}/screens")]
    public async Task<IActionResult> CreateScreenAsync(Guid menuId, [FromBody] ScreenUpsertRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        // Check menu exists
        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT 1 FROM menu_nodes WHERE id = $1";
            checkCmd.Parameters.AddWithValue(menuId);
            var exists = await checkCmd.ExecuteScalarAsync(ct);
            if (exists is null) return NotFound(new { message = "Menu not found" });
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO report_screens (menu_id, name, icon, status, sort_order)
            VALUES ($1, $2, $3, $4, $5)
            RETURNING id, created_at
            """;
        cmd.Parameters.AddWithValue(menuId);
        cmd.Parameters.AddWithValue(req.Name ?? "");
        cmd.Parameters.AddWithValue(req.Icon ?? "📊");
        cmd.Parameters.AddWithValue(req.Status ?? "draft");
        cmd.Parameters.AddWithValue(req.SortOrder ?? 0);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        await rdr.ReadAsync(ct);
        return Created($"api/v1/admin/menus/{menuId}/screens/{rdr.GetGuid(0)}", new
        {
            id        = rdr.GetGuid(0),
            createdAt = rdr.GetDateTime(1)
        });
    }

    [HttpPut("{menuId:guid}/screens/{screenId:guid}")]
    public async Task<IActionResult> UpdateScreenAsync(Guid menuId, Guid screenId, [FromBody] ScreenUpsertRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE report_screens SET
                name       = COALESCE($3, name),
                icon       = COALESCE($4, icon),
                status     = COALESCE($5, status),
                sort_order = COALESCE($6, sort_order),
                updated_at = NOW()
            WHERE id = $1 AND menu_id = $2
            RETURNING id, updated_at
            """;
        cmd.Parameters.AddWithValue(screenId);
        cmd.Parameters.AddWithValue(menuId);
        cmd.Parameters.AddWithValue(req.Name is null ? DBNull.Value : (object)req.Name);
        cmd.Parameters.AddWithValue(req.Icon is null ? DBNull.Value : (object)req.Icon);
        cmd.Parameters.AddWithValue(req.Status is null ? DBNull.Value : (object)req.Status);
        cmd.Parameters.AddWithValue(req.SortOrder.HasValue ? (object)req.SortOrder.Value : DBNull.Value);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return Ok(new { id = rdr.GetGuid(0), updatedAt = rdr.GetDateTime(1) });
    }

    [HttpDelete("{menuId:guid}/screens/{screenId:guid}")]
    public async Task<IActionResult> DeleteScreenAsync(Guid menuId, Guid screenId, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM report_screens WHERE id = $1 AND menu_id = $2 RETURNING id";
        cmd.Parameters.AddWithValue(screenId);
        cmd.Parameters.AddWithValue(menuId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return NoContent();
    }

    // ─── Designer save (batch) ────────────────────────────────────────────────

    [HttpPut("{menuId:guid}/screens/{screenId:guid}/save")]
    public async Task<IActionResult> SaveScreenWithWidgetsAsync(Guid menuId, Guid screenId, [FromBody] SaveScreenRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1. Update screen
        await using (var updateCmd = conn.CreateCommand())
        {
            updateCmd.Transaction = tx;
            updateCmd.CommandText = """
                UPDATE report_screens SET
                    name       = $3,
                    icon       = $4,
                    status     = $5,
                    updated_at = NOW()
                WHERE id = $1 AND menu_id = $2
                RETURNING id
                """;
            updateCmd.Parameters.AddWithValue(screenId);
            updateCmd.Parameters.AddWithValue(menuId);
            updateCmd.Parameters.AddWithValue(req.Name);
            updateCmd.Parameters.AddWithValue(req.Icon);
            updateCmd.Parameters.AddWithValue(req.Status);

            var result = await updateCmd.ExecuteScalarAsync(ct);
            if (result is null)
            {
                await tx.RollbackAsync(ct);
                return NotFound();
            }
        }

        // 2. Delete existing widgets
        await using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM screen_widgets WHERE screen_id = $1";
            deleteCmd.Parameters.AddWithValue(screenId);
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        // 3. Insert new widgets
        for (int i = 0; i < (req.Widgets?.Count ?? 0); i++)
        {
            var w = req.Widgets![i];
            await using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO screen_widgets (screen_id, widget_type, title, col_span, sort_order, color, data_source, config)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8::jsonb)
                """;
            insertCmd.Parameters.AddWithValue(screenId);
            insertCmd.Parameters.AddWithValue(w.WidgetType ?? "text");
            insertCmd.Parameters.AddWithValue(w.Title ?? "Widget");
            insertCmd.Parameters.AddWithValue(w.ColSpan ?? 6);
            insertCmd.Parameters.AddWithValue(i); // sort_order = index
            insertCmd.Parameters.AddWithValue(w.Color ?? "#4f46e5");
            insertCmd.Parameters.AddWithValue(w.DataSource is null ? DBNull.Value : (object)w.DataSource);
            insertCmd.Parameters.AddWithValue(w.Config ?? "{}");
            await insertCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return Ok(new { id = screenId, savedAt = DateTime.UtcNow });
    }

    // ─── Widget CRUD ──────────────────────────────────────────────────────────

    [HttpGet("{menuId:guid}/screens/{screenId:guid}/widgets")]
    public async Task<IActionResult> ListWidgetsAsync(Guid menuId, Guid screenId, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT w.id, w.widget_type, w.title, w.col_span, w.sort_order,
                   w.color, w.data_source, w.config, w.created_at
            FROM screen_widgets w
            JOIN report_screens s ON s.id = w.screen_id
            WHERE w.screen_id = $1 AND s.menu_id = $2
            ORDER BY w.sort_order
            """;
        cmd.Parameters.AddWithValue(screenId);
        cmd.Parameters.AddWithValue(menuId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<object>();
        while (await rdr.ReadAsync(ct))
        {
            rows.Add(new
            {
                id         = rdr.GetGuid(0),
                widgetType = rdr.GetString(1),
                title      = rdr.GetString(2),
                colSpan    = rdr.GetInt32(3),
                sortOrder  = rdr.GetInt32(4),
                color      = rdr.GetString(5),
                dataSource = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                config     = rdr.IsDBNull(7) ? "{}" : rdr.GetString(7),
                createdAt  = rdr.GetDateTime(8)
            });
        }
        return Ok(rows);
    }

    [HttpPost("{menuId:guid}/screens/{screenId:guid}/widgets")]
    public async Task<IActionResult> CreateWidgetAsync(Guid menuId, Guid screenId, [FromBody] WidgetUpsertRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        // Verify screen belongs to menu
        await using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT 1 FROM report_screens WHERE id = $1 AND menu_id = $2";
            checkCmd.Parameters.AddWithValue(screenId);
            checkCmd.Parameters.AddWithValue(menuId);
            var exists = await checkCmd.ExecuteScalarAsync(ct);
            if (exists is null) return NotFound(new { message = "Screen not found" });
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO screen_widgets (screen_id, widget_type, title, col_span, sort_order, color, data_source, config)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8::jsonb)
            RETURNING id, created_at
            """;
        cmd.Parameters.AddWithValue(screenId);
        cmd.Parameters.AddWithValue(req.WidgetType ?? "text");
        cmd.Parameters.AddWithValue(req.Title ?? "Widget");
        cmd.Parameters.AddWithValue(req.ColSpan ?? 6);
        cmd.Parameters.AddWithValue(req.SortOrder ?? 0);
        cmd.Parameters.AddWithValue(req.Color ?? "#4f46e5");
        cmd.Parameters.AddWithValue(req.DataSource is null ? DBNull.Value : (object)req.DataSource);
        cmd.Parameters.AddWithValue(req.Config ?? "{}");

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        await rdr.ReadAsync(ct);
        return Created($"api/v1/admin/menus/{menuId}/screens/{screenId}/widgets/{rdr.GetGuid(0)}", new
        {
            id        = rdr.GetGuid(0),
            createdAt = rdr.GetDateTime(1)
        });
    }

    [HttpPut("{menuId:guid}/screens/{screenId:guid}/widgets/{widgetId:guid}")]
    public async Task<IActionResult> UpdateWidgetAsync(Guid menuId, Guid screenId, Guid widgetId, [FromBody] WidgetUpsertRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE screen_widgets SET
                widget_type = COALESCE($3, widget_type),
                title       = COALESCE($4, title),
                col_span    = COALESCE($5, col_span),
                sort_order  = COALESCE($6, sort_order),
                color       = COALESCE($7, color),
                data_source = COALESCE($8, data_source),
                config      = COALESCE($9::jsonb, config)
            WHERE id = $1
              AND screen_id = $2
              AND $2 IN (SELECT id FROM report_screens WHERE menu_id = $10)
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(widgetId);
        cmd.Parameters.AddWithValue(screenId);
        cmd.Parameters.AddWithValue(req.WidgetType is null ? DBNull.Value : (object)req.WidgetType);
        cmd.Parameters.AddWithValue(req.Title is null ? DBNull.Value : (object)req.Title);
        cmd.Parameters.AddWithValue(req.ColSpan.HasValue ? (object)req.ColSpan.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.SortOrder.HasValue ? (object)req.SortOrder.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.Color is null ? DBNull.Value : (object)req.Color);
        cmd.Parameters.AddWithValue(req.DataSource is null ? DBNull.Value : (object)req.DataSource);
        cmd.Parameters.AddWithValue(req.Config is null ? DBNull.Value : (object)req.Config);
        cmd.Parameters.AddWithValue(menuId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return Ok(new { id = rdr.GetGuid(0) });
    }

    [HttpDelete("{menuId:guid}/screens/{screenId:guid}/widgets/{widgetId:guid}")]
    public async Task<IActionResult> DeleteWidgetAsync(Guid menuId, Guid screenId, Guid widgetId, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM screen_widgets
            WHERE id = $1
              AND screen_id = $2
              AND $2 IN (SELECT id FROM report_screens WHERE menu_id = $3)
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(widgetId);
        cmd.Parameters.AddWithValue(screenId);
        cmd.Parameters.AddWithValue(menuId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return NoContent();
    }

    // ─── Permission CRUD ──────────────────────────────────────────────────────

    [HttpGet("{menuId:guid}/permissions")]
    public async Task<IActionResult> ListPermsAsync(Guid menuId, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, menu_id, principal_type, principal_value, can_view, can_export
            FROM menu_permissions
            WHERE menu_id = $1
            ORDER BY principal_type, principal_value
            """;
        cmd.Parameters.AddWithValue(menuId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<object>();
        while (await rdr.ReadAsync(ct))
        {
            rows.Add(new
            {
                id             = rdr.GetGuid(0),
                menuId         = rdr.GetGuid(1),
                principalType  = rdr.GetString(2),
                principalValue = rdr.GetString(3),
                canView        = rdr.GetBoolean(4),
                canExport      = rdr.GetBoolean(5)
            });
        }
        return Ok(rows);
    }

    [HttpPost("{menuId:guid}/permissions")]
    public async Task<IActionResult> AddPermAsync(Guid menuId, [FromBody] PermissionRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO menu_permissions (menu_id, principal_type, principal_value, can_view, can_export)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (menu_id, principal_type, principal_value) DO UPDATE SET
                can_view   = EXCLUDED.can_view,
                can_export = EXCLUDED.can_export
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(menuId);
        cmd.Parameters.AddWithValue(req.PrincipalType);
        cmd.Parameters.AddWithValue(req.PrincipalValue);
        cmd.Parameters.AddWithValue(req.CanView ?? true);
        cmd.Parameters.AddWithValue(req.CanExport ?? false);

        var id = await cmd.ExecuteScalarAsync(ct);
        return Ok(new { id });
    }

    [HttpPut("{menuId:guid}/permissions/{permId:guid}")]
    public async Task<IActionResult> UpdatePermAsync(Guid menuId, Guid permId, [FromBody] PermissionRequest req, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE menu_permissions SET
                can_view   = COALESCE($3, can_view),
                can_export = COALESCE($4, can_export)
            WHERE id = $1 AND menu_id = $2
            RETURNING id
            """;
        cmd.Parameters.AddWithValue(permId);
        cmd.Parameters.AddWithValue(menuId);
        cmd.Parameters.AddWithValue(req.CanView.HasValue ? (object)req.CanView.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(req.CanExport.HasValue ? (object)req.CanExport.Value : DBNull.Value);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return Ok(new { id = rdr.GetGuid(0) });
    }

    [HttpDelete("{menuId:guid}/permissions/{permId:guid}")]
    public async Task<IActionResult> DeletePermAsync(Guid menuId, Guid permId, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM menu_permissions WHERE id = $1 AND menu_id = $2 RETURNING id";
        cmd.Parameters.AddWithValue(permId);
        cmd.Parameters.AddWithValue(menuId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return NotFound();
        return NoContent();
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static string Slugify(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        var result = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        result = result.Replace('đ', 'd').Replace('Đ', 'd');
        result = Regex.Replace(result, @"[^a-z0-9/]", "-");
        result = Regex.Replace(result, @"-{2,}", "-");
        return result.Trim('-');
    }
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record MenuUpsertRequest(
    string? Name,
    string? Slug,
    string? Icon,
    string? Description,
    Guid?   ParentId,
    int?    SortOrder,
    bool?   IsVisible
);

public sealed record ScreenUpsertRequest(
    string? Name,
    string? Icon,
    string? Status,
    int?    SortOrder
);

public sealed record WidgetUpsertRequest(
    string? WidgetType,
    string? Title,
    string? Color,
    string? DataSource,
    string? Config,
    int?    ColSpan,
    int?    SortOrder
);

public sealed record WidgetSaveItem(
    string? WidgetType,
    string? Title,
    string? Color,
    string? DataSource,
    string? Config,
    int?    ColSpan,
    int?    SortOrder
);

public sealed record SaveScreenRequest(
    string             Name,
    string             Icon,
    string             Status,
    List<WidgetSaveItem> Widgets
);

public sealed record PermissionRequest(
    string PrincipalType,
    string PrincipalValue,
    bool?  CanView,
    bool?  CanExport
);
