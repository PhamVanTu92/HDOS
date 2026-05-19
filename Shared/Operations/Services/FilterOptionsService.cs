using ReportingPlatform.Adapters.Abstractions;
using ReportingPlatform.Adapters.Models;
using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Operations.Services;

/// <summary>
/// Fetches dropdown filter options from a datasource for a given filter_dropdown widget.
/// Extracted from <see cref="ReportingPlatform.Resolver.Core.DashboardResolver"/> Phase-4 pre-fetch logic.
/// </summary>
public sealed class FilterOptionsService
{
    private readonly IDatasourceAdapterFactory _adapters;
    private readonly ILogger<FilterOptionsService> _logger;

    public FilterOptionsService(
        IDatasourceAdapterFactory adapters,
        ILogger<FilterOptionsService> logger)
    {
        _adapters = adapters;
        _logger   = logger;
    }

    public async Task<IReadOnlyList<FilterOption>> FetchAsync(
        string tenantId,
        WidgetDefinition widget,
        DatasourceDefinition datasource,
        IReadOnlyDictionary<string, JsonElement> filters,
        string? search,
        CancellationToken ct = default)
    {
        if (!widget.VisualConfig.HasValue ||
            widget.VisualConfig.Value.ValueKind != JsonValueKind.Object ||
            !widget.VisualConfig.Value.TryGetProperty("optionsSource", out var os) ||
            os.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        os.TryGetProperty("source",   out var srcEl);
        os.TryGetProperty("valueKey", out var vkEl);
        os.TryGetProperty("labelKey", out var lkEl);

        var sourceName = srcEl.GetString()  ?? string.Empty;
        var valueKey   = vkEl.GetString()   ?? "value";
        var labelKey   = lkEl.GetString()   ?? valueKey;

        var adapter = _adapters.Resolve(datasource);
        var request = new AdapterRequest
        {
            TenantId   = tenantId,
            Datasource = datasource with
            {
                ConnectionConfig = JsonDocument.Parse(
                    $"{{\"mode\":\"querybuilder\",\"source\":\"{sourceName}\"}}").RootElement
            },
            Filters = filters,
        };

        var result = await adapter.FetchAsync(request, ct);

        var options = result.Rows.Select(row =>
        {
            var valEl   = row.TryGetValue(valueKey, out var v) ? v : default;
            var labelEl = row.TryGetValue(labelKey, out var l) ? l : valEl;
            return new FilterOption
            {
                Value = valEl.ValueKind   == JsonValueKind.String ? valEl.GetString()!   : valEl.GetRawText(),
                Label = labelEl.ValueKind == JsonValueKind.String ? labelEl.GetString()! : labelEl.GetRawText(),
            };
        }).ToList();

        // Apply client-side search filter when search term provided
        if (!string.IsNullOrEmpty(search))
        {
            options = options
                .Where(o =>
                    o.Label.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    o.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return options;
    }
}
