namespace ReportingPlatform.Contracts.Validation;

public sealed record ValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<ValidationError> Errors { get; init; }

    public static readonly ValidationResult Success =
        new() { IsValid = true, Errors = [] };

    public static ValidationResult Failure(IReadOnlyList<ValidationError> errors) =>
        new() { IsValid = false, Errors = errors };
}
