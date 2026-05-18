namespace ReportingPlatform.Contracts.RenderPayloads;

public sealed record WidgetMeta
{
    public required string RenderContractVersion { get; init; }
    public required string GeneratedAt { get; init; }
    public bool FromCache { get; init; }
    public long ElapsedMs { get; init; }

    // SignalR channel for WidgetStale events: "widget:{dashboardCode}:{widgetId}"
    public required string SubscribeChannel { get; init; }
}
