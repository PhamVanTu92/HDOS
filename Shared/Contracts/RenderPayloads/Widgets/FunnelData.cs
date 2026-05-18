using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record FunnelData
{
    public required IReadOnlyList<FunnelStep> Steps { get; init; }
}
