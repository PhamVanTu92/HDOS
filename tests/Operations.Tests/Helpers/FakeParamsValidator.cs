using ReportingPlatform.Contracts.Validation;

namespace ReportingPlatform.Operations.Tests.Helpers;

internal sealed class FakeParamsValidator : IParamsValidator
{
    private readonly bool _isValid;
    private readonly IReadOnlyList<ValidationError>? _errors;

    private FakeParamsValidator(bool isValid, IReadOnlyList<ValidationError>? errors = null)
    {
        _isValid = isValid;
        _errors  = errors;
    }

    public Task<ValidationResult> ValidateAsync(
        string operationPattern, JsonElement @params, CancellationToken cancellationToken = default) =>
        Task.FromResult(_isValid
            ? Contracts.Validation.ValidationResult.Success
            : Contracts.Validation.ValidationResult.Failure(_errors ?? [
                new ValidationError { Field = "params", Message = "validation failed", Code = "INVALID" }
            ]));

    public static FakeParamsValidator AlwaysValid()   => new(isValid: true);
    public static FakeParamsValidator AlwaysInvalid() => new(isValid: false);

    public static FakeParamsValidator InvalidWith(params ValidationError[] errors) =>
        new(isValid: false, errors: errors);
}
