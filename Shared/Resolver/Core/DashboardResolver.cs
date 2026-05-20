namespace ReportingPlatform.Resolver.Core;

/// <summary>
/// Orchestrates the two-phase render pipeline:
///   Phase 1 — Pre-fetch: deduplicated adapter calls for filter_dropdown options sources.
///   Phase 2 — Fan-out: <see cref="SemaphoreSlim"/>-bounded <see cref="Task.WhenAll"/> per widget.
///
/// Widget isolation: individual widget failures are captured and surfaced as <see cref="WidgetError"/>
/// within their <see cref="WidgetEnvelope"/> — they do NOT abort the entire render.
/// </summary>
public sealed class DashboardResolver : IDashboardResolver
{
    private const string RenderContractVersion = "1.0";

    private readonly IDashboardDefinitionRepository _definitions;
    private readonly IDatasourceAdapterFactory _adapters;
    private readonly TransformerRegistry _transformers;
    private readonly IComputedColumnEngine _engine;
    private readonly WidgetCacheService _cache;
    private readonly IOptions<ResolverOptions> _options;
    private readonly ILogger<DashboardResolver> _logger;

    private readonly SemaphoreSlim _widgetSemaphore;

    public DashboardResolver(
        IDashboardDefinitionRepository definitions,
        IDatasourceAdapterFactory adapters,
        TransformerRegistry transformers,
        IComputedColumnEngine engine,
        WidgetCacheService cache,
        IOptions<ResolverOptions> options,
        ILogger<DashboardResolver> logger)
    {
        _definitions = definitions;
        _adapters    = adapters;
        _transformers = transformers;
        _engine      = engine;
        _cache       = cache;
        _options     = options;
        _logger      = logger;

        _widgetSemaphore = new SemaphoreSlim(options.Value.MaxConcurrentWidgets);
    }

    public async Task<DashboardRenderPayload> RenderAsync(
        string tenantId,
        string dashboardCode,
        IReadOnlyDictionary<string, JsonElement> filters,
        IReadOnlyDictionary<string, TablePaginationParams>? tableParams = null,
        CancellationToken ct = default,
        string? callerRequestId = null,
        string? callerUserId = null,
        DateTimeOffset? callerDeadline = null,
        bool callerWantsProgress = false)
    {
        var sw = Stopwatch.StartNew();

        var defResult = await _definitions.GetAsync(tenantId, dashboardCode, ct)
            ?? throw new AdapterException("DASHBOARD_NOT_FOUND", dashboardCode);

        var (dashboard, version) = defResult;
        var widgets = dashboard.Widgets ?? [];

        // Fetch all datasource definitions referenced by widgets in one batch
        var dsIds = widgets.Select(w => w.DatasourceId).Distinct().ToList();
        var datasources = await _definitions.GetDatasourcesAsync(tenantId, dsIds, ct);

        // Filter canonicalization for cache key
        var canonical   = FilterCanonicalizer.Canonicalize(filters);
        var filtersHash = FilterCanonicalizer.Hash(canonical);

        // Phase 1: pre-fetch dropdown options
        var dropdownOptions = await PreFetchDropdownOptionsAsync(
            tenantId, widgets, datasources, filters, ct);

        // Phase 2: fan-out widget rendering
        var tasks = widgets.Select(widget => RenderWidgetAsync(
            tenantId, dashboardCode, version, widget,
            datasources.TryGetValue(widget.DatasourceId, out var ds) ? ds : null,
            filters, tableParams, dropdownOptions, filtersHash,
            callerRequestId, callerUserId, callerDeadline, callerWantsProgress, ct));

        var envelopes = await Task.WhenAll(tasks);

        _logger.LogInformation(
            "Dashboard rendered: tenant={TenantId} code={Code} v={Version} widgets={Count} elapsed={ElapsedMs}ms",
            tenantId, dashboardCode, version, envelopes.Length, sw.ElapsedMilliseconds);

        return new DashboardRenderPayload
        {
            DashboardCode  = dashboardCode,
            Version        = version,
            RequestId      = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
            RenderedAt     = DateTimeOffset.UtcNow.ToString("O"),
            Widgets        = envelopes,
            AppliedFilters = filters,
            RefreshPolicy  = BuildRefreshPolicy(dashboard),
        };
    }

    // --- Phase 1: Pre-fetch dropdown options ---

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<FilterOption>>> PreFetchDropdownOptionsAsync(
        string tenantId,
        IReadOnlyList<WidgetDefinition> widgets,
        IReadOnlyDictionary<string, DatasourceDefinition> datasources,
        IReadOnlyDictionary<string, JsonElement> filters,
        CancellationToken ct)
    {
        // Collect filter_dropdown widgets with optionsSource
        var dropdowns = widgets
            .Where(w => w.ChartType.Equals("filter_dropdown", StringComparison.OrdinalIgnoreCase) &&
                        w.VisualConfig.HasValue &&
                        w.VisualConfig.Value.ValueKind == JsonValueKind.Object &&
                        w.VisualConfig.Value.TryGetProperty("optionsSource", out _))
            .ToList();

        if (dropdowns.Count == 0)
            return new Dictionary<string, IReadOnlyList<FilterOption>>();

        // Deduplicate by (datasourceId, source) canonical key
        var deduped = new Dictionary<string, (WidgetDefinition Widget, string CanonicalKey)>(
            StringComparer.Ordinal);

        foreach (var w in dropdowns)
        {
            w.VisualConfig!.Value.TryGetProperty("optionsSource", out var os);
            if (os.ValueKind != JsonValueKind.Object) continue;
            os.TryGetProperty("source", out var srcEl);
            var src = srcEl.GetString() ?? string.Empty;

            var canonKey = $"{w.DatasourceId}:{src}";
            deduped.TryAdd(canonKey, (w, canonKey));
        }

        // Fetch each unique options source (under semaphore)
        var results = new Dictionary<string, IReadOnlyList<FilterOption>>(StringComparer.Ordinal);
        var fetchedByKey = new Dictionary<string, IReadOnlyList<FilterOption>>(StringComparer.Ordinal);

        foreach (var (canonKey, (widget, _)) in deduped)
        {
            if (!datasources.TryGetValue(widget.DatasourceId, out var ds)) continue;

            await _widgetSemaphore.WaitAsync(ct);
            try
            {
                var options = await FetchDropdownOptionsAsync(tenantId, widget, ds, filters, ct);
                fetchedByKey[canonKey] = options;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pre-fetch failed for dropdown {WidgetId}", widget.WidgetId);
                fetchedByKey[canonKey] = [];
            }
            finally
            {
                _widgetSemaphore.Release();
            }
        }

        // Map back to widgetId → options
        foreach (var w in dropdowns)
        {
            w.VisualConfig!.Value.TryGetProperty("optionsSource", out var os);
            os.TryGetProperty("source", out var srcEl);
            var src = srcEl.GetString() ?? string.Empty;
            var canonKey = $"{w.DatasourceId}:{src}";
            if (fetchedByKey.TryGetValue(canonKey, out var opts))
                results[w.WidgetId] = opts;
        }

        return results;
    }

    private async Task<IReadOnlyList<FilterOption>> FetchDropdownOptionsAsync(
        string tenantId,
        WidgetDefinition widget,
        DatasourceDefinition ds,
        IReadOnlyDictionary<string, JsonElement> filters,
        CancellationToken ct)
    {
        widget.VisualConfig!.Value.TryGetProperty("optionsSource", out var os);
        os.TryGetProperty("source", out var srcEl);
        var sourceName = srcEl.GetString() ?? string.Empty;

        // Rewrite the datasource ConnectionConfig to target the optionsSource
        // (the widget datasource may be for main data; optionsSource is a separate source)
        var adapter = _adapters.Resolve(ds);
        var request = new Adapters.Models.AdapterRequest
        {
            TenantId   = tenantId,
            Datasource = ds with
            {
                ConnectionConfig = JsonDocument.Parse(
                    $"{{\"mode\":\"querybuilder\",\"source\":\"{sourceName}\"}}").RootElement
            },
            Filters = filters,
        };

        var result  = await adapter.FetchAsync(request, ct);
        var vc      = widget.VisualConfig!.Value;

        string valueKey = vc.ValueKind == JsonValueKind.Object &&
                          vc.TryGetProperty("optionsSource", out var osEl) &&
                          osEl.TryGetProperty("valueKey", out var vk)
            ? vk.GetString() ?? "value"
            : "value";

        string labelKey = vc.ValueKind == JsonValueKind.Object &&
                          vc.TryGetProperty("optionsSource", out var osEl2) &&
                          osEl2.TryGetProperty("labelKey", out var lk)
            ? lk.GetString() ?? valueKey
            : valueKey;

        return result.Rows.Select(row =>
        {
            var valEl   = row.TryGetValue(valueKey, out var v) ? v : default;
            var labelEl = row.TryGetValue(labelKey, out var l) ? l : valEl;
            return new FilterOption
            {
                Value = valEl.ValueKind == JsonValueKind.String ? valEl.GetString()! : valEl.GetRawText(),
                Label = labelEl.ValueKind == JsonValueKind.String ? labelEl.GetString()! : labelEl.GetRawText(),
            };
        }).ToList();
    }

    // --- Phase 2: Widget rendering ---

    private async Task<WidgetEnvelope> RenderWidgetAsync(
        string tenantId,
        string dashCode,
        string version,
        WidgetDefinition widget,
        DatasourceDefinition? datasource,
        IReadOnlyDictionary<string, JsonElement> filters,
        IReadOnlyDictionary<string, TablePaginationParams>? tableParams,
        IReadOnlyDictionary<string, IReadOnlyList<FilterOption>> dropdownOptions,
        string filtersHash,
        string? callerRequestId,
        string? callerUserId,
        DateTimeOffset? callerDeadline,
        bool callerWantsProgress,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw        = Stopwatch.StartNew();

        // Cache lookup
        var cacheKey = WidgetCacheService.MakeKey(tenantId, dashCode, version, widget.WidgetId, filtersHash);
        var cached   = await _cache.GetAsync(cacheKey, ct);
        if (cached is not null)
            return cached;

        await _widgetSemaphore.WaitAsync(ct);
        try
        {
            return await RenderWidgetCoreAsync(
                tenantId, dashCode, version, widget, datasource,
                filters, tableParams, dropdownOptions,
                callerRequestId, callerUserId, callerDeadline, callerWantsProgress,
                cacheKey, startedAt, sw, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Widget render failed: tenant={TenantId} widgetId={WidgetId}", tenantId, widget.WidgetId);

            return BuildErrorEnvelope(widget, ex, startedAt, sw.ElapsedMilliseconds, dashCode);
        }
        finally
        {
            _widgetSemaphore.Release();
        }
    }

    private async Task<WidgetEnvelope> RenderWidgetCoreAsync(
        string tenantId,
        string dashCode,
        string version,
        WidgetDefinition widget,
        DatasourceDefinition? datasource,
        IReadOnlyDictionary<string, JsonElement> filters,
        IReadOnlyDictionary<string, TablePaginationParams>? tableParams,
        IReadOnlyDictionary<string, IReadOnlyList<FilterOption>> dropdownOptions,
        string? callerRequestId,
        string? callerUserId,
        DateTimeOffset? callerDeadline,
        bool callerWantsProgress,
        string cacheKey,
        DateTimeOffset startedAt,
        Stopwatch sw,
        CancellationToken ct)
    {
        var transformer = _transformers.Resolve(widget.ChartType)
            ?? throw new AdapterException("UNKNOWN_CHART_TYPE", widget.ChartType);

        // Per-widget timeout
        var vc         = widget.VisualConfig ?? default;
        var timeoutMs  = vc.ValueKind == JsonValueKind.Object &&
                         vc.TryGetProperty("timeoutMs", out var toProp) &&
                         toProp.ValueKind == JsonValueKind.Number
                            ? (int)toProp.GetDouble()
                            : _options.Value.DefaultWidgetTimeoutMs;

        using var widgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        widgetCts.CancelAfter(timeoutMs);

        // Adapter call (skipped for layout widgets with no datasource)
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows = [];
        long? totalRows = null;

        if (datasource is not null)
        {
            var tableParams_ = tableParams?.TryGetValue(widget.WidgetId, out var tp) == true ? tp : null;

            var request = new Adapters.Models.AdapterRequest
            {
                TenantId        = tenantId,
                Datasource      = datasource,
                Filters         = filters,
                Pagination      = tableParams_,
                ParentRequestId     = callerRequestId,
                UserId              = callerUserId,
                ParentDeadline      = callerDeadline,
                ParentWantsProgress = callerWantsProgress,
            };

            var adapter = _adapters.Resolve(datasource);
            var result  = await adapter.FetchAsync(request, widgetCts.Token);
            rows        = result.Rows;
            totalRows   = result.TotalRows;
        }

        // ComputedColumnEngine
        var computedCols = GetComputedColumns(widget.VisualConfig);
        if (computedCols.Count > 0)
            rows = _engine.Apply(rows, computedCols);

        // Transformer
        var ctx = new WidgetRenderContext
        {
            Widget         = widget,
            Filters        = filters,
            DropdownOptions = dropdownOptions.Count > 0 ? dropdownOptions : null,
        };

        var data = await transformer.TransformAsync(rows, totalRows, ctx, widgetCts.Token);

        var envelope = new WidgetEnvelope
        {
            WidgetId  = widget.WidgetId,
            ChartType = widget.ChartType,
            Title     = widget.Title,
            Subtitle  = widget.Subtitle,
            VisualConfig    = widget.VisualConfig ?? default,
            Data      = data,
            IsEmpty   = rows.Count == 0,
            Meta      = new WidgetMeta
            {
                RenderContractVersion = RenderContractVersion,
                GeneratedAt           = startedAt.ToString("O"),
                FromCache             = false,
                ElapsedMs             = sw.ElapsedMilliseconds,
                SubscribeChannel      = $"widget:{dashCode}:{widget.WidgetId}",
            },
        };

        // Cache if datasource has CacheSeconds > 0
        var ttl = datasource?.CacheSeconds ?? 0;
        if (ttl > 0)
            await _cache.SetAsync(cacheKey, envelope, ttl);

        return envelope;
    }

    private static IReadOnlyList<TableColumn> GetComputedColumns(JsonElement? vc)
    {
        if (vc is null || vc.Value.ValueKind != JsonValueKind.Object)
            return [];

        if (!vc.Value.TryGetProperty("columns", out var cols) ||
            cols.ValueKind != JsonValueKind.Array)
            return [];

        return cols.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object &&
                        c.TryGetProperty("computed", out var comp) &&
                        comp.ValueKind == JsonValueKind.String)
            .Select(c => new TableColumn
            {
                Key        = c.TryGetProperty("key", out var k) ? k.GetString() ?? string.Empty : string.Empty,
                Label      = c.TryGetProperty("label", out var l) ? l.GetString() ?? string.Empty : string.Empty,
                Type       = c.TryGetProperty("type", out var t) ? t.GetString() ?? "number" : "number",
                Computed   = c.TryGetProperty("computed", out var comp2) ? comp2.GetString() : null,
                ComputedOn = c.TryGetProperty("computedOn", out var co) ? co.GetString() : null,
                Sortable   = false,
            })
            .ToList();
    }

    private static WidgetEnvelope BuildErrorEnvelope(
        WidgetDefinition widget,
        Exception ex,
        DateTimeOffset startedAt,
        long elapsedMs,
        string dashCode)
    {
        var (code, message) = ex is AdapterException ae
            ? (ae.ErrorCode, ae.Message)
            : ("WIDGET_RENDER_ERROR", ex.Message);

        return new WidgetEnvelope
        {
            WidgetId  = widget.WidgetId,
            ChartType = widget.ChartType,
            Title     = widget.Title,
            Subtitle  = widget.Subtitle,
            IsEmpty   = true,
            Error     = new WidgetError { Code = code, Message = message },
            Meta = new WidgetMeta
            {
                RenderContractVersion = RenderContractVersion,
                GeneratedAt           = startedAt.ToString("O"),
                ElapsedMs             = elapsedMs,
                SubscribeChannel      = $"widget:{dashCode}:{widget.WidgetId}",
            },
        };
    }

    private static RefreshPolicy BuildRefreshPolicy(DashboardDefinition dashboard)
    {
        var rp = dashboard.RefreshPolicy;
        if (rp is null) return new RefreshPolicy { Mode = "manual" };

        return new RefreshPolicy
        {
            Mode            = rp.Mode,
            IntervalSeconds = rp.IntervalSeconds,
            DebounceMs      = rp.DebounceMs,
        };
    }
}
