namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record ClickAction
{
    // "open_dashboard"
    public required string Type { get; init; }

    public required string TargetDashboardCode { get; init; }

    // Template token map — values like "{{clicked.x}}", "{{filters.region}}"
    public required IReadOnlyDictionary<string, string> FilterMapping { get; init; }

    // "drilldown" | "new_tab" | "replace"
    public required string OpenMode { get; init; }
}
