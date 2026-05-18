namespace ReportingPlatform.Contracts.RenderPayloads.Shared;

public sealed record SeriesPoint
{
    // String (category/time) or number — JsonElement handles both.
    public required JsonElement X { get; init; }

    // null means "no data at this point" (gap, not zero).
    public double? Y { get; init; }
}
