namespace ReportingPlatform.Metadata.Results;

public sealed record UpsertResult
{
    public required long Id      { get; init; }
    public required int  Version { get; init; }
}
