namespace ReportingPlatform.Contracts.Validation;

public interface IParamsValidator
{
    Task<ValidationResult> ValidateAsync(
        string operationPattern,
        JsonElement @params,
        CancellationToken cancellationToken = default);
}
