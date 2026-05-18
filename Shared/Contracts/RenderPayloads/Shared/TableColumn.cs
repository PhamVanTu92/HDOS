using ReportingPlatform.Contracts.Enums;

namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record TableColumn
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    // "string" | "number" | "date" | "boolean" | "badge" | "currency"
    public required string Type { get; init; }

    public bool Sortable { get; init; }
    public bool Filterable { get; init; }

    // "text" | "range" | "select" | "date" — null when Filterable = false
    public string? FilterType { get; init; }

    public string? Format { get; init; }

    // One of ComputedTransform constants — null for non-computed columns.
    public string? Computed { get; init; }

    // Source column key for computed columns.
    public string? ComputedOn { get; init; }

    // "sum" | "avg" | "count" | "min" | "max"
    public string? Aggregation { get; init; }

    public int? Width { get; init; }

    // "left" | "right" | null
    public string? Frozen { get; init; }

    public bool Visible { get; init; } = true;

    // "left" | "center" | "right"
    public string? Align { get; init; }
}
