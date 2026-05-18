namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record PivotMeasure
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    // "sum" | "count" | "avg" | "min" | "max"
    public required string Aggregate { get; init; }

    public string? Format { get; init; }
}
