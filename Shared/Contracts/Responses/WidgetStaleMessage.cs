using MessagePack;

namespace ReportingPlatform.Contracts.Responses;

[MessagePackObject]
public sealed record WidgetStaleMessage
{
    // Format: "widget:{dashboardCode}:{widgetId}"
    [Key("channel")]
    public required string Channel { get; init; }

    [Key("hint")]
    public required WidgetStaleHint Hint { get; init; }
}
