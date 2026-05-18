namespace ReportingPlatform.Contracts.Definitions;

public sealed record DashboardDefinition
{
    public required string DashboardCode { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<WidgetDefinition>? Widgets { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? DefaultFilters { get; init; }
    public RefreshPolicyDefinition? RefreshPolicy { get; init; }
}

public sealed record WidgetDefinition
{
    public required string WidgetId { get; init; }
    public required string ChartType { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string DatasourceId { get; init; }
    public IReadOnlyList<string>? AllowedChartTypes { get; init; }
    public JsonElement? VisualConfig { get; init; }
    public JsonElement? InteractionConfig { get; init; }
}

public sealed record RefreshPolicyDefinition
{
    // "interval" | "manual" | "event"
    public required string Mode { get; init; }
    public int? IntervalSeconds { get; init; }
    public int? DebounceMs { get; init; }
}
