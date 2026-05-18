using ReportingPlatform.Contracts.RenderPayloads.Shared;

namespace ReportingPlatform.Contracts.RenderPayloads.Widgets;

public sealed record TabContainerData
{
    public required IReadOnlyList<TabDefinition> Tabs { get; init; }
}
