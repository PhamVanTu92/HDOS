namespace ReportingPlatform.Metadata.Results;

public sealed record DeleteResult
{
    public required bool Deleted { get; init; }
}
