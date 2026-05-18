using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads;

public sealed record DashboardRenderPayload
{
    public required string DashboardCode { get; init; }
    public required string Version { get; init; }
    public required string RequestId { get; init; }
    public required string RenderedAt { get; init; }
    public required IReadOnlyList<WidgetEnvelope> Widgets { get; init; }
    // Echo of resolved global filters applied to this render.
    public required IReadOnlyDictionary<string, JsonElement> AppliedFilters { get; init; }
    public required RefreshPolicy RefreshPolicy { get; init; }
}
