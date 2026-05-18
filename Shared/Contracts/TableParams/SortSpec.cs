namespace ReportingPlatform.Contracts.TableParams;

public sealed record SortSpec
{
    public required string Key { get; init; }

    // "asc" | "desc"
    public required string Direction { get; init; }
}
