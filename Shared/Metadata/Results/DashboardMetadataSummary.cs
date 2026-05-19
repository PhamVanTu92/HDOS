namespace ReportingPlatform.Metadata.Results;

public sealed record DashboardMetadataSummary
{
    public required string DashboardCode { get; init; }
    public required string Title         { get; init; }
    public string?         Description   { get; init; }
    public required int    Version       { get; init; }
}
