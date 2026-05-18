using MessagePack;

namespace ReportingPlatform.Contracts.Responses;

[MessagePackObject]
public sealed record WidgetStaleHint
{
    // One of WidgetStaleReasons constants.
    [Key("reason")]
    public required string Reason { get; init; }

    [Key("updatedAt")]
    public required string UpdatedAt { get; init; }
}
