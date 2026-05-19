namespace ReportingPlatform.Operations.Context.Params;

public sealed record WidgetTableExportParams
{
    public required string DashboardCode { get; init; }
    public required string WidgetId      { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Filters { get; init; }

    // "csv" | "xlsx"
    public required string Format { get; init; }
}
