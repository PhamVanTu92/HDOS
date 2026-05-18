namespace ReportingPlatform.Contracts.RenderPayloads;

public sealed record WidgetError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
}
