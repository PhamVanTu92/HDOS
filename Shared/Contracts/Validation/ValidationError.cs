namespace ReportingPlatform.Contracts.Validation;

public sealed record ValidationError
{
    public required string Field { get; init; }
    public required string Message { get; init; }
    public required string Code { get; init; }
}
