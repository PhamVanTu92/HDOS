namespace ReportingPlatform.Contracts.RenderPayloads;

public sealed record DashboardPayload
{
    public required string DashboardCode { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Category { get; init; }
    public required string Version { get; init; }
    public required string UpdatedAt { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    // Ordered list of widget IDs in layout order.
    public required IReadOnlyList<string> WidgetIds { get; init; }
    // Per-widget static config: keys are widgetId.
    public required IReadOnlyDictionary<string, JsonElement> WidgetConfigs { get; init; }
}
