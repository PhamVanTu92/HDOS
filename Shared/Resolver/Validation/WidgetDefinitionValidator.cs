using ReportingPlatform.Contracts.Validation;

namespace ReportingPlatform.Resolver.Validation;

/// <summary>
/// Validates widget definitions against nine rules (R1–R9).
/// All errors are collected (not fail-fast) and returned as a single <see cref="ValidationResult"/>.
/// </summary>
internal sealed class WidgetDefinitionValidator : IWidgetDefinitionValidator
{
    private readonly TransformerRegistry _transformers;
    private readonly IQueryableSourceRepository _sources;

    private static readonly int[] ValidTimeoutRange = [1_000, 300_000];

    public WidgetDefinitionValidator(
        TransformerRegistry transformers,
        IQueryableSourceRepository sources)
    {
        _transformers = transformers;
        _sources      = sources;
    }

    public async Task<ValidationResult> ValidateAsync(
        string tenantId,
        DashboardDefinition dashboard,
        IReadOnlyDictionary<string, DatasourceDefinition> datasources,
        CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        var widgets = dashboard.Widgets ?? [];

        // R9: unique widget IDs (pure in-memory, O(n))
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in widgets)
        {
            if (!seenIds.Add(w.WidgetId))
                errors.Add(Error($"widgets[{w.WidgetId}].widgetId",
                    $"Duplicate widgetId '{w.WidgetId}' in dashboard.", "DUPLICATE_WIDGET_ID"));
        }

        // Per-widget rules (R1–R8)
        foreach (var widget in widgets)
        {
            var vc = widget.VisualConfig ?? default;

            // R1: chartType must be a supported transformer type
            if (_transformers.Resolve(widget.ChartType) is null)
                errors.Add(Error($"widgets[{widget.WidgetId}].chartType",
                    $"Unknown chartType '{widget.ChartType}'.", "UNKNOWN_CHART_TYPE"));

            // R5: timeoutMs must be in [1000, 300000] when present
            if (vc.ValueKind == JsonValueKind.Object &&
                vc.TryGetProperty("timeoutMs", out var toProp) &&
                toProp.ValueKind == JsonValueKind.Number)
            {
                var ms = (int)toProp.GetDouble();
                if (ms < ValidTimeoutRange[0] || ms > ValidTimeoutRange[1])
                    errors.Add(Error($"widgets[{widget.WidgetId}].visualConfig.timeoutMs",
                        $"timeoutMs {ms} is outside valid range [1000, 300000].", "INVALID_TIMEOUT"));
            }

            // R6: gauge requires min, max, and max > min
            if (widget.ChartType.Equals("gauge", StringComparison.OrdinalIgnoreCase))
            {
                var hasMin = vc.ValueKind == JsonValueKind.Object && vc.TryGetProperty("min", out var minEl) && minEl.ValueKind == JsonValueKind.Number;
                var hasMax = vc.ValueKind == JsonValueKind.Object && vc.TryGetProperty("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number;

                if (!hasMin || !hasMax)
                {
                    errors.Add(Error($"widgets[{widget.WidgetId}].visualConfig",
                        "Gauge widget requires numeric 'min' and 'max' in visualConfig.", "INVALID_GAUGE_CONFIG"));
                }
                else
                {
                    vc.TryGetProperty("min", out var mnEl);
                    vc.TryGetProperty("max", out var mxEl);
                    if (mxEl.GetDouble() <= mnEl.GetDouble())
                        errors.Add(Error($"widgets[{widget.WidgetId}].visualConfig",
                            "Gauge 'max' must be greater than 'min'.", "INVALID_GAUGE_CONFIG"));
                }
            }

            // Skip SQL-specific rules for non-SQL datasources
            if (!datasources.TryGetValue(widget.DatasourceId, out var ds))
                continue;

            if (!ds.Type.Equals("sql", StringComparison.OrdinalIgnoreCase))
                continue;

            // Parse mode from ConnectionConfig
            string? mode = null;
            if (ds.ConnectionConfig.ValueKind == JsonValueKind.Object &&
                ds.ConnectionConfig.TryGetProperty("mode", out var modeProp))
                mode = modeProp.GetString();

            // R4: advanced_table requires querybuilder or raw mode (not timescale)
            if (widget.ChartType.Equals("advanced_table", StringComparison.OrdinalIgnoreCase))
            {
                if (mode is "timescale")
                    errors.Add(Error($"widgets[{widget.WidgetId}].datasourceId",
                        "advanced_table does not support timescale datasource mode.", "PAGINATION_NOT_SUPPORTED"));
            }

            // R3 + R7: source existence checks via queryable_sources whitelist
            if (mode is "querybuilder")
            {
                string? sourceName = null;
                if (ds.ConnectionConfig.TryGetProperty("source", out var srcProp))
                    sourceName = srcProp.GetString();

                if (!string.IsNullOrWhiteSpace(sourceName))
                {
                    var src = await _sources.GetAsync(tenantId, sourceName, ct);
                    if (src is null)
                        errors.Add(Error($"widgets[{widget.WidgetId}].datasourceId",
                            $"QueryBuilder source '{sourceName}' not found in queryable_sources.", "SOURCE_NOT_FOUND"));
                }
            }

            // R7: filter_dropdown optionsSource existence
            if (widget.ChartType.Equals("filter_dropdown", StringComparison.OrdinalIgnoreCase) &&
                vc.ValueKind == JsonValueKind.Object &&
                vc.TryGetProperty("optionsSource", out var osProp) &&
                osProp.ValueKind == JsonValueKind.Object &&
                osProp.TryGetProperty("source", out var osSrcProp))
            {
                var optSrc = osSrcProp.GetString();
                if (!string.IsNullOrWhiteSpace(optSrc))
                {
                    var src = await _sources.GetAsync(tenantId, optSrc, ct);
                    if (src is null)
                        errors.Add(Error($"widgets[{widget.WidgetId}].visualConfig.optionsSource.source",
                            $"Options source '{optSrc}' not found in queryable_sources.", "OPTIONS_SOURCE_NOT_FOUND"));
                }
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Failure(errors);
    }

    private static ValidationError Error(string field, string message, string code) =>
        new() { Field = field, Message = message, Code = code };
}
