namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record AxisDefinition
{
    // "category" | "time" | "number"
    public required string Type { get; init; }

    public required string Label { get; init; }
    public string? Format { get; init; }
}
