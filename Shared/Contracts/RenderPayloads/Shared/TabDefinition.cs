namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record TabDefinition
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<string> WidgetIds { get; init; }
    public bool Default { get; init; }
}
