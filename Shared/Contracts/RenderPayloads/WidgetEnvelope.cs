using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads;

public sealed record WidgetEnvelope
{
    public required string WidgetId { get; init; }
    public required string ChartType { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    // Renderer-specific visual config (colors, thresholds, axes, etc.).
    public JsonElement VisualConfig { get; init; }
    public InteractionConfig? Interactions { get; init; }
    // Strongly-typed payload serialized to JsonElement by the handler before assembly.
    public JsonElement Data { get; init; }
    public required WidgetMeta Meta { get; init; }
    public bool IsEmpty { get; init; }
    public WidgetError? Error { get; init; }
}
