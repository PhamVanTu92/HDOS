namespace ReportingPlatform.Operations.Context.Params;

public sealed record WidgetDrillContextParams
{
    public required string   SourceDashboard  { get; init; }
    public required string   WidgetId         { get; init; }
    public required JsonElement ClickedData   { get; init; }
    public required string   TargetDashboard  { get; init; }
    public JsonElement?      CurrentFilters   { get; init; }
}
