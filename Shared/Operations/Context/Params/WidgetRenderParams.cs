using ReportingPlatform.Contracts.TableParams;

namespace ReportingPlatform.Operations.Context.Params;

public sealed record WidgetRenderParams
{
    public required string DashboardCode { get; init; }
    public required string WidgetId      { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Filters     { get; init; }
    public IReadOnlyDictionary<string, TablePaginationParams>? TableParams { get; init; }
}
