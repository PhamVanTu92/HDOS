namespace ReportingPlatform.Operations.Context.Params;

public sealed record WidgetFilterOptionsParams
{
    public required string DashboardCode { get; init; }
    public required string WidgetId      { get; init; }
    public string?         Search        { get; init; }
}
