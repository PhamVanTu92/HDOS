namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record InteractionConfig
{
    public ClickAction? OnClickDataPoint { get; init; }
    public ClickAction? OnClickRow { get; init; }
    public ClickAction? OnClickCell { get; init; }
    public IReadOnlyList<DrillPathLevel>? DrillPath { get; init; }
}
