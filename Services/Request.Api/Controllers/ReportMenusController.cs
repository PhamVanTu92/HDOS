using System.Text.Json;
using Npgsql;
using StackExchange.Redis;

namespace ReportingPlatform.RequestApi.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize]
public sealed class ReportMenusController : ControllerBase
{
    private readonly NpgsqlDataSource      _db;
    private readonly IConnectionMultiplexer _redis;

    public ReportMenusController(NpgsqlDataSource db, IConnectionMultiplexer redis)
    {
        _db    = db;
        _redis = redis;
    }

    // GET api/v1/reports/menus
    [HttpGet("menus")]
    public async Task<IActionResult> GetAccessibleMenusAsync(CancellationToken ct)
    {
        var isAdmin = User.IsInRole("admin");
        var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var roles   = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT m.id, m.name, m.slug, m.icon, m.description,
                            m.parent_id, m.sort_order
            FROM menu_nodes m
            LEFT JOIN menu_permissions p ON p.menu_id = m.id
            WHERE m.is_visible = true
              AND (
                    $1
                    OR (
                          p.can_view = true
                          AND (
                                (p.principal_type = 'role' AND p.principal_value = ANY($2))
                             OR (p.principal_type = 'user' AND p.principal_value = $3)
                              )
                       )
                  )
            ORDER BY m.parent_id NULLS FIRST, m.sort_order
            """;
        cmd.Parameters.AddWithValue(isAdmin);
        cmd.Parameters.AddWithValue(roles);
        cmd.Parameters.AddWithValue(userId);

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
                sortOrder   = rdr.GetInt32(6)
            });
        }
        return Ok(rows);
    }

    // GET api/v1/reports/menus/{slug}
    [HttpGet("menus/{slug}")]
    public async Task<IActionResult> GetMenuBySlugAsync(string slug, CancellationToken ct)
    {
        var isAdmin = User.IsInRole("admin");
        var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var roles   = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        await using var conn = await _db.OpenConnectionAsync(ct);

        // Fetch menu with permission check
        Guid   menuId;
        string menuName, menuIcon, menuSlug;
        string? menuDescription;

        await using (var menuCmd = conn.CreateCommand())
        {
            menuCmd.CommandText = """
                SELECT DISTINCT m.id, m.name, m.slug, m.icon, m.description
                FROM menu_nodes m
                LEFT JOIN menu_permissions p ON p.menu_id = m.id
                WHERE m.slug = $1
                  AND m.is_visible = true
                  AND (
                        $2
                        OR (
                              p.can_view = true
                              AND (
                                    (p.principal_type = 'role' AND p.principal_value = ANY($3))
                                 OR (p.principal_type = 'user' AND p.principal_value = $4)
                                  )
                           )
                      )
                LIMIT 1
                """;
            menuCmd.Parameters.AddWithValue(slug);
            menuCmd.Parameters.AddWithValue(isAdmin);
            menuCmd.Parameters.AddWithValue(roles);
            menuCmd.Parameters.AddWithValue(userId);

            await using var rdr = await menuCmd.ExecuteReaderAsync(ct);
            if (!await rdr.ReadAsync(ct)) return NotFound();

            menuId          = rdr.GetGuid(0);
            menuName        = rdr.GetString(1);
            menuSlug        = rdr.GetString(2);
            menuIcon        = rdr.GetString(3);
            menuDescription = rdr.IsDBNull(4) ? null : rdr.GetString(4);
        }

        // Fetch published screens
        await using var screenCmd = conn.CreateCommand();
        screenCmd.CommandText = """
            SELECT id, name, icon, sort_order
            FROM report_screens
            WHERE menu_id = $1 AND status = 'published'
            ORDER BY sort_order
            """;
        screenCmd.Parameters.AddWithValue(menuId);

        await using var screenRdr = await screenCmd.ExecuteReaderAsync(ct);
        var screens = new List<object>();
        while (await screenRdr.ReadAsync(ct))
        {
            screens.Add(new
            {
                id        = screenRdr.GetGuid(0),
                name      = screenRdr.GetString(1),
                icon      = screenRdr.GetString(2),
                sortOrder = screenRdr.GetInt32(3)
            });
        }

        return Ok(new
        {
            id          = menuId,
            name        = menuName,
            slug        = menuSlug,
            icon        = menuIcon,
            description = menuDescription,
            screens
        });
    }

    // GET api/v1/reports/menus/{slug}/screens/{screenId}
    [HttpGet("menus/{slug}/screens/{screenId:guid}")]
    public async Task<IActionResult> GetScreenAsync(string slug, Guid screenId, CancellationToken ct)
    {
        var isAdmin = User.IsInRole("admin");
        var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var roles   = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        await using var conn = await _db.OpenConnectionAsync(ct);

        // Permission check on menu (by slug) + fetch screen
        Guid   menuId;
        string menuName, menuSlug, screenName, screenIcon, refreshMode;
        int    refreshIntervalS;

        await using (var screenCmd = conn.CreateCommand())
        {
            screenCmd.CommandText = """
                SELECT s.id, s.name, s.icon, m.id, m.name, m.slug,
                       s.refresh_mode, s.refresh_interval_s
                FROM report_screens s
                JOIN menu_nodes m ON m.id = s.menu_id
                LEFT JOIN menu_permissions p ON p.menu_id = m.id
                WHERE s.id = $1
                  AND m.slug = $2
                  AND m.is_visible = true
                  AND (
                        $3
                        OR (
                              p.can_view = true
                              AND (
                                    (p.principal_type = 'role' AND p.principal_value = ANY($4))
                                 OR (p.principal_type = 'user' AND p.principal_value = $5)
                                  )
                           )
                      )
                LIMIT 1
                """;
            screenCmd.Parameters.AddWithValue(screenId);
            screenCmd.Parameters.AddWithValue(slug);
            screenCmd.Parameters.AddWithValue(isAdmin);
            screenCmd.Parameters.AddWithValue(roles);
            screenCmd.Parameters.AddWithValue(userId);

            await using var rdr = await screenCmd.ExecuteReaderAsync(ct);
            if (!await rdr.ReadAsync(ct)) return NotFound();

            screenName       = rdr.GetString(1);
            screenIcon       = rdr.GetString(2);
            menuId           = rdr.GetGuid(3);
            menuName         = rdr.GetString(4);
            menuSlug         = rdr.GetString(5);
            refreshMode      = rdr.GetString(6);
            refreshIntervalS = rdr.GetInt32(7);
        }

        // Fetch widgets
        await using var widgetCmd = conn.CreateCommand();
        widgetCmd.CommandText = """
            SELECT id, widget_type, title, col_span, sort_order, color, data_source, config
            FROM screen_widgets
            WHERE screen_id = $1
            ORDER BY sort_order
            """;
        widgetCmd.Parameters.AddWithValue(screenId);

        await using var widgetRdr = await widgetCmd.ExecuteReaderAsync(ct);
        var widgets = new List<object>();
        while (await widgetRdr.ReadAsync(ct))
        {
            widgets.Add(new
            {
                id         = widgetRdr.GetGuid(0),
                widgetType = widgetRdr.GetString(1),
                title      = widgetRdr.GetString(2),
                colSpan    = widgetRdr.GetInt32(3),
                sortOrder  = widgetRdr.GetInt32(4),
                color      = widgetRdr.GetString(5),
                dataSource = widgetRdr.IsDBNull(6) ? null : widgetRdr.GetString(6),
                config     = widgetRdr.IsDBNull(7) ? "{}" : widgetRdr.GetString(7)
            });
        }

        return Ok(new
        {
            screenId,
            name             = screenName,
            icon             = screenIcon,
            menuId,
            menuName,
            menuSlug,
            refreshMode,
            refreshIntervalS,
            widgets
        });
    }

    // POST api/v1/reports/screens/{screenId}/stale
    // Publish a WidgetStale SSE event so all browsers in SSE mode refresh this screen.
    // Published to rp:sse-global-event → BroadcastAll → no widgetChannel subscription needed
    // on the client. Safe to call from Excel Provider or any authenticated system.
    [HttpPost("screens/{screenId:guid}/stale")]
    public async Task<IActionResult> NotifyStaleAsync(Guid screenId, CancellationToken ct)
    {
        var channel = $"screen:{screenId}";
        var payload = JsonSerializer.Serialize(new
        {
            eventType = "WidgetStale",
            channel,
            reason    = "data.updated",
            updatedAt = DateTime.UtcNow.ToString("O"),
        });

        // Broadcast to ALL connected SSE clients — they filter by channel on the frontend.
        await _redis.GetDatabase().PublishAsync(
            RedisChannel.Literal("rp:sse-global-event"),
            payload);

        return NoContent();
    }
}
