namespace ReportingPlatform.Adapters.Models;

public sealed record ColumnDescriptor
{
    public required string Key { get; init; }

    // Normalised type: "string" | "number" | "date" | "boolean"
    public required string Type { get; init; }
}
