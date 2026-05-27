using Npgsql;
using System.Text.Json;

namespace ReportingPlatform.RequestApi.Controllers;

/// <summary>
/// Public module endpoints — served to all authenticated users.
/// Powers the config-driven sidebar and /m/:slug module pages.
/// </summary>
[ApiController]
[Route("api/v1/modules")]
[Authorize]
public sealed class ModuleController : ControllerBase
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<ModuleController> _logger;

    public ModuleController(NpgsqlDataSource db, ILogger<ModuleController> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── GET /api/v1/modules ──────────────────────────────────────────────────
    // Returns sidebar structure: groups[] with modules[] filtered by user roles.

    [HttpGet]
    public async Task<IActionResult> GetSidebarAsync(CancellationToken ct)
    {
        // Extract realm roles from JWT claims (Keycloak pattern)
        var userRoles = User.Claims
            .Where(c => c.Type == "realm_access_roles" || c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var conn = await _db.OpenConnectionAsync(ct);

        // Load groups
        var groups = new List<SidebarGroupDto>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, slug, label, icon, sort_order
                FROM module_groups
                WHERE is_visible = true
                ORDER BY sort_order
                """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                groups.Add(new SidebarGroupDto(
                    r.GetGuid(0), r.GetString(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.GetInt32(4),
                    new List<SidebarModuleDto>()));
        }

        // Load modules and filter by role
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, group_id, slug, label, icon, required_roles, sort_order
                FROM modules
                WHERE is_visible = true AND is_active = true
                ORDER BY group_id, sort_order
                """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var groupId       = r.GetGuid(1);
                var requiredRoles = r.IsDBNull(5) ? null : (string[]?)r.GetValue(5);

                // Access check: null roles = all users; array = must have at least one
                if (requiredRoles is { Length: > 0 } &&
                    !requiredRoles.Any(role => userRoles.Contains(role)))
                    continue;

                var group = groups.FirstOrDefault(g => g.Id == groupId);
                if (group is null) continue;

                group.Modules.Add(new SidebarModuleDto(
                    r.GetGuid(0), r.GetString(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetInt32(6)));
            }
        }

        // Remove empty groups
        var result = groups.Where(g => g.Modules.Count > 0).ToList();
        return Ok(result);
    }

    // ── GET /api/v1/modules/{slug}/layout ───────────────────────────────────
    // Returns tab + widget layout for a module page.

    [HttpGet("{slug}/layout")]
    public async Task<IActionResult> GetLayoutAsync(string slug, CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        // Get module
        Guid moduleId;
        int? refreshIntervalSeconds = null;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, refresh_interval_seconds FROM modules
                WHERE slug = $1 AND is_active = true
                """;
            cmd.Parameters.AddWithValue(slug);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return NotFound(new { error = $"Module '{slug}' not found." });
            moduleId               = r.GetGuid(0);
            refreshIntervalSeconds = r.IsDBNull(1) ? null : r.GetInt32(1);
        }

        // Get tabs
        var tabs = new List<ModuleTabDto>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, slug, label, sort_order, is_default
                FROM module_tabs
                WHERE module_id = $1
                ORDER BY sort_order
                """;
            cmd.Parameters.AddWithValue(moduleId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                tabs.Add(new ModuleTabDto(
                    r.GetGuid(0), r.GetString(1), r.GetString(2),
                    r.GetInt32(3), r.GetBoolean(4),
                    new List<WidgetLayoutDto>()));
        }

        // Get widgets for all tabs
        if (tabs.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT tab_id, widget_key, title, subtitle, chart_type,
                       grid_x, grid_y, grid_w, grid_h,
                       operation_pattern, provider_id,
                       params_template::text, visual_config::text,
                       filter_bindings, interactions::text,
                       filter_key, is_visible, sort_order
                FROM widgets
                WHERE tab_id = ANY($1) AND is_visible = true
                ORDER BY tab_id, sort_order
                """;
            var tabIds = tabs.Select(t => t.Id).ToArray();
            cmd.Parameters.AddWithValue(tabIds);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var tabId = r.GetGuid(0);
                var tab   = tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab is null) continue;

                tab.Widgets.Add(new WidgetLayoutDto(
                    WidgetKey:       r.GetString(1),
                    Title:           r.IsDBNull(2) ? null : r.GetString(2),
                    Subtitle:        r.IsDBNull(3) ? null : r.GetString(3),
                    ChartType:       r.GetString(4),
                    GridX:           r.GetInt32(5),
                    GridY:           r.GetInt32(6),
                    GridW:           r.GetInt32(7),
                    GridH:           r.GetInt32(8),
                    OperationPattern: r.IsDBNull(9) ? null : r.GetString(9),
                    ProviderId:      r.IsDBNull(10) ? null : r.GetString(10),
                    ParamsTemplate:  r.IsDBNull(11) ? "{}" : r.GetString(11),
                    VisualConfig:    r.IsDBNull(12) ? "{}" : r.GetString(12),
                    FilterBindings:  r.IsDBNull(13) ? Array.Empty<string>() : (string[])r.GetValue(13),
                    Interactions:    r.IsDBNull(14) ? "{}" : r.GetString(14),
                    FilterKey:       r.IsDBNull(15) ? null : r.GetString(15)));
            }
        }

        return Ok(new ModuleLayoutDto(slug, refreshIntervalSeconds, tabs));
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record SidebarGroupDto(
    Guid   Id,
    string Slug,
    string Label,
    string? Icon,
    int    SortOrder,
    List<SidebarModuleDto> Modules);

public sealed record SidebarModuleDto(
    Guid   Id,
    string Slug,
    string Label,
    string? Icon,
    int    SortOrder);

public sealed record ModuleLayoutDto(
    string Slug,
    int?   RefreshIntervalSeconds,
    List<ModuleTabDto> Tabs);

public sealed record ModuleTabDto(
    Guid   Id,
    string Slug,
    string Label,
    int    SortOrder,
    bool   IsDefault,
    List<WidgetLayoutDto> Widgets);

public sealed record WidgetLayoutDto(
    string  WidgetKey,
    string? Title,
    string? Subtitle,
    string  ChartType,
    int     GridX,
    int     GridY,
    int     GridW,
    int     GridH,
    string? OperationPattern,
    string? ProviderId,
    string  ParamsTemplate,
    string  VisualConfig,
    string[] FilterBindings,
    string  Interactions,
    string? FilterKey);
